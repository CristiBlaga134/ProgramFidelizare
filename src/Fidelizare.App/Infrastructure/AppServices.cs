using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;
using Fidelizare.Data.Sqlite;

namespace Fidelizare.App.Infrastructure;

/// <summary>
/// Locul unde se construiesc și se țin serviciile aplicației.
///
/// Programul are puține componente și o singură fereastră activă, deci un
/// container de injecție ar adăuga complexitate fără să rezolve ceva. Aici se
/// vede dintr-o privire ce depinde de ce.
/// </summary>
public sealed class AppServices
{
    private AppServices(string databasePath)
    {
        DatabasePath = databasePath;

        var factory = new SqliteUnitOfWorkFactory(databasePath);

        Factory = factory;
        Clock = SystemClock.Instance;
        Loyalty = new LoyaltyService(factory, Clock);
        Auth = new AuthService(factory, Clock);
        Reporting = new ReportingService(factory, Clock);
        Demo = new DemoDataGenerator(factory, Loyalty, Clock);
        Transfer = new DataTransferService(factory, Loyalty, Clock);
    }

    /// <summary>Instanța curentă, creată o singură dată la pornirea aplicației.</summary>
    public static AppServices Current { get; private set; } = null!;

    public string DatabasePath { get; }

    public IUnitOfWorkFactory Factory { get; }

    public IClock Clock { get; }

    public LoyaltyService Loyalty { get; }

    public AuthService Auth { get; }

    public ReportingService Reporting { get; }

    public DemoDataGenerator Demo { get; }

    public DataTransferService Transfer { get; }

    /// <summary>Utilizatorul autentificat. Null înainte de autentificare.</summary>
    public AppUser? CurrentUser { get; private set; }

    /// <summary>
    /// Utilizatorul autentificat, pentru operațiunile care presupun o sesiune activă.
    /// Dacă lipsește, este o eroare de program, nu o situație de tratat în interfață.
    /// </summary>
    public AppUser RequireUser => CurrentUser
        ?? throw new InvalidOperationException("Operațiunea a fost cerută fără un utilizator autentificat.");

    public static AppServices Initialize(string databasePath)
    {
        Current = new AppServices(databasePath);
        return Current;
    }

    public void SignIn(AppUser user) => CurrentUser = user;

    public void SignOut() => CurrentUser = null;

    /// <summary>Reîncarcă datele utilizatorului curent, după schimbarea parolei sau a rolului.</summary>
    public void RefreshCurrentUser()
    {
        if (CurrentUser is null)
            return;

        using var uow = Factory.Create();
        var fresh = uow.Users.GetById(CurrentUser.Id);

        if (fresh is not null)
            CurrentUser = fresh;
    }

    /// <summary>Există măcar un cont? Dacă nu, aplicația pornește în modul de configurare inițială.</summary>
    public bool HasAnyUser()
    {
        using var uow = Factory.Create();
        return uow.Users.GetAll().Count > 0;
    }
}
