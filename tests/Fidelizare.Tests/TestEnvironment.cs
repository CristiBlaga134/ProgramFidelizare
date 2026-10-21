using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;
using Fidelizare.Data.Sqlite;

namespace Fidelizare.Tests;

/// <summary>Ceas controlabil, ca regulile de expirare să poată fi testate fără a aștepta un an.</summary>
internal sealed class FakeClock : IClock
{
    public FakeClock(DateTime now) => Now = now;

    public DateTime Now { get; set; }

    /// <summary>
    /// Ziua curentă. IClock o oferă ca membru implicit de interfață, adică vizibil
    /// doar prin interfață; testele lucrează cu tipul concret, deci este redeclarată.
    /// </summary>
    public DateTime Today => Now.Date;

    public void Advance(TimeSpan delta) => Now += delta;

    public void AdvanceMonths(int months) => Now = Now.AddMonths(months);
}

/// <summary>
/// O bază de date proaspătă într-un fișier temporar, cu serviciile pornite peste ea.
/// Fiecare test primește propriul fișier, deci testele nu se influențează între ele.
/// </summary>
internal sealed class TestEnvironment : IDisposable
{
    private readonly string _folder;

    public TestEnvironment(DateTime? now = null)
    {
        _folder = Path.Combine(Path.GetTempPath(), "fidelizare-teste", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        Clock = new FakeClock(now ?? new DateTime(2026, 3, 10, 11, 0, 0));
        Factory = new SqliteUnitOfWorkFactory(Path.Combine(_folder, "test.db"));
        Loyalty = new LoyaltyService(Factory, Clock);
        Auth = new AuthService(Factory, Clock);
        Transfer = new DataTransferService(Factory, Loyalty, Clock);

        Admin = CreateUser("admin", "Administrator", UserRole.Admin);
        Cashier = CreateUser("casier", "Casier de test", UserRole.Cashier);
    }

    public FakeClock Clock { get; }

    public SqliteUnitOfWorkFactory Factory { get; }

    public LoyaltyService Loyalty { get; }

    public AuthService Auth { get; }

    public DataTransferService Transfer { get; }

    public AppUser Admin { get; }

    public AppUser Cashier { get; }

    /// <summary>Creează un cont direct în baza de date, ocolind regula „doar adminul creează conturi”.</summary>
    public AppUser CreateUser(string username, string displayName, UserRole role, string password = "parola123")
    {
        var (hash, salt, iterations) = PasswordHasher.Create(password);

        var user = new AppUser
        {
            Username = displayName,
            PasswordHash = hash,
            PasswordSalt = salt,
            PasswordIterations = iterations,
            Role = role,
            IsActive = true,
            CreatedAt = Clock.Now,
        };

        using var uow = Factory.Create();
        user.Id = uow.Users.Insert(user);
        uow.Commit();

        return user;
    }

    public Customer Enroll(string lastName = "Popescu", string firstName = "Ioana", string? phone = "0722123456",
        DateTime? birthDate = null)
    {
        var result = Loyalty.EnrollCustomer(new Customer
        {
            LastName = lastName,
            FirstName = firstName,
            Phone = phone,
            BirthDate = birthDate,
        }, Admin);

        Assert.True(result.Success, result.Error);
        return result.Value!;
    }

    public Purchase Buy(int customerId, decimal amount, DateTime? at = null, string? receipt = null)
    {
        var result = Loyalty.RecordPurchase(new PurchaseRequest
        {
            CustomerId = customerId,
            GrossAmount = amount,
            OccurredAt = at,
            ReceiptNumber = receipt,
        }, Cashier);

        Assert.True(result.Success, result.Error);
        return result.Value!.Purchase;
    }

    public int Balance(int customerId) => Loyalty.GetSummary(customerId)!.PointsBalance;

    public void SetRules(Action<LoyaltyRules> configure)
    {
        var rules = Loyalty.GetRules();
        configure(rules);
        var result = Loyalty.SaveRules(rules, Admin);
        Assert.True(result.Success, result.Error);
    }

    /// <summary>Un fisier temporar in folderul testului, sters odata cu el.</summary>
    public string TempFile(string name) => Path.Combine(_folder, name);

    public void Dispose()
    {
        // Conexiunile din pool trebuie închise, altfel fișierul rămâne blocat pe Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Un fișier temporar rămas în urmă nu justifică picarea testului.
        }
    }
}
