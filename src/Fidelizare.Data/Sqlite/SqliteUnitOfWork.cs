using Fidelizare.Core.Abstractions;
using Fidelizare.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Sqlite;

/// <summary>
/// Deschide unități de lucru peste un fișier SQLite și se asigură, o singură dată
/// pe durata rulării, că structura bazei de date este la zi.
/// </summary>
public sealed class SqliteUnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly string _connectionString;
    private readonly bool _networkPath;
    private readonly Lock _gate = new();
    private bool _initialized;

    /// <summary>
    /// Câte unități de lucru sunt deschise pe firul de execuție curent.
    ///
    /// Există pentru a prinde o greșeală ușor de făcut și greu de diagnosticat:
    /// deschiderea unei a doua unități de lucru în timp ce prima este încă activă.
    /// Pentru că fiecare ia din start blocajul de scriere, a doua ar aștepta după
    /// prima, care așteaptă după apelul în curs — adică o blocare completă,
    /// vizibilă abia după expirarea temporizării. Mai bine o eroare imediată,
    /// care spune exact ce s-a întâmplat.
    /// </summary>
    [ThreadStatic]
    private static int _openOnThread;

    public SqliteUnitOfWorkFactory(string databasePath)
    {
        DatabasePath = databasePath;
        _networkPath = DatabaseLocation.IsNetworkPath(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Pool-ul de conexiuni ține fișierul deschis între operațiuni, ceea ce
            // scutește de costul redeschiderii la fiecare căutare de la casă.
            Pooling = true,
            // Așteaptă dacă altcineva scrie, în loc să întoarcă imediat eroare.
            DefaultTimeout = 15,
        }.ToString();
    }

    public string DatabasePath { get; }

    public IUnitOfWork Create()
    {
        if (_openOnThread > 0)
        {
            throw new InvalidOperationException(
                "Există deja o unitate de lucru deschisă pe acest fir de execuție. " +
                "Închide-o înainte de a deschide alta, sau folosește repository-urile " +
                "unității existente. Deschiderea a două unități în paralel ar bloca aplicația.");
        }

        EnsureInitialized();

        var connection = new SqliteConnection(_connectionString);

        try
        {
            connection.Open();
            ApplyConnectionPragmas(connection);

            // Tranzacție imediată: blocarea de scriere se ia din start, nu la prima
            // scriere. Fără asta, două case care citesc și apoi scriu simultan pot
            // ajunge la eroare de blocare la jumătatea operațiunii, cu tranzacția
            // deja pe jumătate construită.
            var transaction = connection.BeginTransaction(deferred: false);

            _openOnThread++;
            return new SqliteUnitOfWork(connection, transaction, () => _openOnThread--);
        }
        catch (Exception)
        {
            connection.Dispose();
            throw;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_gate)
        {
            if (_initialized)
                return;

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            ApplyDurablePragmas(connection);
            ApplyConnectionPragmas(connection);
            DatabaseSchema.Initialize(connection);

            _initialized = true;
        }
    }

    /// <summary>Setări care se scriu în fișier și rămân valabile pentru toate conexiunile ulterioare.</summary>
    private void ApplyDurablePragmas(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();

        // Jurnalul WAL permite citiri în paralel cu scrierea, dar se bazează pe
        // maparea fișierului în memorie, ceea ce nu funcționează corect pe un
        // share de rețea. Acolo se rămâne la jurnalul clasic.
        command.CommandText = _networkPath
            ? "PRAGMA journal_mode = DELETE;"
            : "PRAGMA journal_mode = WAL;";
        command.ExecuteNonQuery();

        // NORMAL împreună cu WAL rezistă la o cădere a aplicației; doar o cădere
        // de curent în milisecunda nepotrivită ar putea pierde ultima tranzacție.
        // Compromisul merită: FULL ar însemna o scriere pe disc la fiecare bon.
        command.CommandText = _networkPath
            ? "PRAGMA synchronous = FULL;"
            : "PRAGMA synchronous = NORMAL;";
        command.ExecuteNonQuery();
    }

    /// <summary>Setări care se aplică fiecărei conexiuni în parte.</summary>
    private static void ApplyConnectionPragmas(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 15000;
            """;
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// O tranzacție deschisă peste o conexiune SQLite, împreună cu toate
/// repository-urile care lucrează în interiorul ei.
/// </summary>
internal sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly Action _onDisposed;
    private bool _committed;
    private bool _disposed;

    private ICustomerRepository? _customers;
    private IPurchaseRepository? _purchases;
    private IPointsRepository? _points;
    private IRewardRepository? _rewards;
    private IRedemptionRepository? _redemptions;
    private ITierRepository? _tiers;
    private ICampaignRepository? _campaigns;
    private ICustomFieldRepository? _customFields;
    private ISettingsRepository? _settings;
    private IUserRepository? _users;
    private IAuditRepository? _audit;

    public SqliteUnitOfWork(SqliteConnection connection, SqliteTransaction transaction, Action onDisposed)
    {
        _connection = connection;
        _transaction = transaction;
        _onDisposed = onDisposed;
    }

    public ICustomerRepository Customers => _customers ??= new CustomerRepository(_connection, _transaction);
    public IPurchaseRepository Purchases => _purchases ??= new PurchaseRepository(_connection, _transaction);
    public IPointsRepository Points => _points ??= new PointsRepository(_connection, _transaction);
    public IRewardRepository Rewards => _rewards ??= new RewardRepository(_connection, _transaction);
    public IRedemptionRepository Redemptions => _redemptions ??= new RedemptionRepository(_connection, _transaction);
    public ITierRepository Tiers => _tiers ??= new TierRepository(_connection, _transaction);
    public ICampaignRepository Campaigns => _campaigns ??= new CampaignRepository(_connection, _transaction);
    public ICustomFieldRepository CustomFields => _customFields ??= new CustomFieldRepository(_connection, _transaction);
    public ISettingsRepository Settings => _settings ??= new SettingsRepository(_connection, _transaction);
    public IUserRepository Users => _users ??= new UserRepository(_connection, _transaction);
    public IAuditRepository Audit => _audit ??= new AuditRepository(_connection, _transaction);

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _transaction.Commit();
        _committed = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Fără Commit explicit, tranzacția se anulează. Așa, o excepție apărută la
        // mijlocul unei operațiuni nu lasă în urmă o cumpărare fără puncte.
        if (!_committed)
        {
            try
            {
                _transaction.Rollback();
            }
            catch (Exception)
            {
                // Dacă tranzacția a fost deja închisă de driver, nu mai e nimic de anulat.
            }
        }

        _transaction.Dispose();
        _connection.Dispose();
        _onDisposed();
    }
}
