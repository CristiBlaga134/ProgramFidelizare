using Fidelizare.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Tests;

/// <summary>
/// Trecerea de la două nume pe cont la unul singur.
///
/// Migrarea atinge date deja existente, deci merită verificată pe forma veche a
/// bazei, nu doar pe una nouă: un magazin care actualizează programul nu trebuie
/// să rămână cu conturi pe care nu se mai poate autentifica.
/// </summary>
public class UserNameMigrationTests : IDisposable
{
    private readonly string _folder;
    private readonly string _path;

    public UserNameMigrationTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "fidelizare-migrare", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "vechi.db");
    }

    /// <summary>Construiește tabelul de conturi în forma dinaintea migrării.</summary>
    private void CreateVersion2Database(params (string Username, string Display)[] users)
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE Users (
                    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username           TEXT    NOT NULL COLLATE NOCASE,
                    DisplayName        TEXT    NOT NULL,
                    PasswordHash       TEXT    NOT NULL,
                    PasswordSalt       TEXT    NOT NULL,
                    PasswordIterations INTEGER NOT NULL,
                    Role               INTEGER NOT NULL,
                    IsActive           INTEGER NOT NULL DEFAULT 1,
                    MustChangePassword INTEGER NOT NULL DEFAULT 0,
                    CreatedAt          TEXT    NOT NULL,
                    LastLoginAt        TEXT    NULL
                );

                CREATE UNIQUE INDEX IX_Users_Username ON Users (Username);

                -- Pașii de după migrarea numelor modifică vânzările, deci tabelul
                -- trebuie să existe, ca în orice bază reală de la versiunea 2.
                CREATE TABLE Purchases (
                    Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId              INTEGER NOT NULL,
                    ReceiptNumber           TEXT    NULL,
                    GrossAmountBani         INTEGER NOT NULL,
                    DiscountAmountBani      INTEGER NOT NULL DEFAULT 0,
                    NetAmountBani           INTEGER NOT NULL,
                    PointsEarned            INTEGER NOT NULL DEFAULT 0,
                    TierAtPurchase          TEXT    NOT NULL DEFAULT '',
                    MultiplierMilli         INTEGER NOT NULL DEFAULT 1000,
                    OccurredAt              TEXT    NOT NULL,
                    RecordedByUserId        INTEGER NULL,
                    RecordedByUserName      TEXT    NULL,
                    IsVoided                INTEGER NOT NULL DEFAULT 0,
                    VoidedAt                TEXT    NULL,
                    VoidReason              TEXT    NULL,
                    CampaignName            TEXT    NULL,
                    CampaignMultiplierMilli INTEGER NOT NULL DEFAULT 1000
                );
                """;

            create.ExecuteNonQuery();
        }

        foreach (var (username, display) in users)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO Users
                    (Username, DisplayName, PasswordHash, PasswordSalt, PasswordIterations, Role, CreatedAt)
                VALUES (@u, @d, 'x', 'y', 1000, 3, '2026-01-01 10:00:00');
                """;

            insert.Parameters.AddWithValue("@u", username);
            insert.Parameters.AddWithValue("@d", display);
            insert.ExecuteNonQuery();
        }

        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version = 2;";
        version.ExecuteNonQuery();
    }

    private List<string> ReadNamesAfterMigration()
    {
        // Deschiderea prin fabrica obișnuită declanșează migrarea.
        var factory = new SqliteUnitOfWorkFactory(_path);

        using var uow = factory.Create();
        return uow.Users.GetAll().Select(u => u.Username).OrderBy(n => n).ToList();
    }

    [Fact]
    public void Numele_afisat_devine_numele_contului()
    {
        CreateVersion2Database(("cristi", "Cristian Popescu"), ("ana", "Ana Ionescu"));

        var names = ReadNamesAfterMigration();

        Assert.Equal(["Ana Ionescu", "Cristian Popescu"], names);
    }

    [Fact]
    public void Contul_fara_nume_afisat_isi_pastreaza_numele_de_autentificare()
    {
        CreateVersion2Database(("casa1", ""), ("ana", "Ana Ionescu"));

        var names = ReadNamesAfterMigration();

        Assert.Equal(["Ana Ionescu", "casa1"], names);
    }

    [Fact]
    public void Doua_conturi_cu_acelasi_nume_afisat_nu_se_ciocnesc()
    {
        // Numele afișate nu erau unice, deci migrarea trebuie să rezolve conflictul
        // fără să piardă niciun cont.
        CreateVersion2Database(("ana1", "Ana Popescu"), ("ana2", "Ana Popescu"));

        var names = ReadNamesAfterMigration();

        Assert.Equal(2, names.Count);
        Assert.Equal(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Ana Popescu", names);
    }

    [Fact]
    public void Coloana_veche_dispare_dupa_migrare()
    {
        CreateVersion2Database(("cristi", "Cristian Popescu"));
        _ = ReadNamesAfterMigration();

        SqliteConnection.ClearAllPools();

        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'DisplayName';";

        Assert.Equal(0L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void Migrarea_nu_se_repeta_la_a_doua_deschidere()
    {
        CreateVersion2Database(("cristi", "Cristian Popescu"));

        Assert.Equal(["Cristian Popescu"], ReadNamesAfterMigration());
        SqliteConnection.ClearAllPools();

        // A doua deschidere găsește schema deja la zi și nu mai atinge nimic.
        Assert.Equal(["Cristian Popescu"], ReadNamesAfterMigration());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
