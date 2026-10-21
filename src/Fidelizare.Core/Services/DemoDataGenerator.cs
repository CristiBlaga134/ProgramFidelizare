using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;

namespace Fidelizare.Core.Services;

/// <summary>
/// Umple baza de date cu clienți și istoric de cumpărături plauzibile.
///
/// Serveşte la două lucruri: să se poată vedea cum arată programul înainte de a
/// avea clienți reali și să existe date pentru o prezentare. Trece prin aceleași
/// metode ca funcționarea normală, deci istoricul generat este coerent — loturi,
/// registru și niveluri sunt exact cum ar fi ieșit din munca de zi cu zi.
/// </summary>
public sealed class DemoDataGenerator
{
    private static readonly string[] LastNames =
    [
        "Popescu", "Ionescu", "Dumitrescu", "Georgescu", "Stan", "Constantinescu", "Marin",
        "Diaconu", "Munteanu", "Barbu", "Nistor", "Șerban", "Țăranu", "Radu", "Oprea",
        "Călinescu", "Moldoveanu", "Ivan", "Lungu", "Voicu", "Neagu", "Stoica", "Dobre",
    ];

    private static readonly string[] FemaleNames =
    [
        "Maria", "Elena", "Ioana", "Andreea", "Cristina", "Alexandra", "Gabriela",
        "Raluca", "Simona", "Diana", "Mihaela", "Roxana", "Bianca", "Ancuța",
    ];

    private static readonly string[] MaleNames =
    [
        "Andrei", "Mihai", "Alexandru", "Cristian", "Vlad", "Radu", "George",
        "Bogdan", "Ionuț", "Ștefan", "Adrian", "Marius", "Sorin", "Gheorghiță",
    ];

    private readonly IUnitOfWorkFactory _factory;
    private readonly LoyaltyService _loyalty;
    private readonly IClock _clock;

    public DemoDataGenerator(IUnitOfWorkFactory factory, LoyaltyService loyalty, IClock clock)
    {
        _factory = factory;
        _loyalty = loyalty;
        _clock = clock;
    }

    /// <summary>
    /// Creează clienții și istoricul lor. Refuză dacă există deja clienți, ca să
    /// nu se amestece date inventate peste date reale dintr-un magazin în funcțiune.
    /// </summary>
    public OperationResult<int> Generate(AppUser actor, int customerCount = 60, int? seed = null)
    {
        if (!actor.CanConfigureProgram)
            return OperationResult<int>.Fail("Doar administratorul poate genera date demonstrative.");

        if (customerCount is < 1 or > 500)
            return OperationResult<int>.Fail("Numărul de clienți trebuie să fie între 1 și 500.");

        using (var uow = _factory.Create())
        {
            var existing = uow.Customers.Count(new CustomerQuery { IncludeInactive = true });
            if (existing > 0)
            {
                return OperationResult<int>.Fail(
                    $"Baza de date conține deja {existing} clienți. Datele demonstrative se pot genera " +
                    "doar pe o bază goală, ca să nu se amestece cu date reale.");
            }
        }

        // Sămânța fixă face generarea repetabilă: aceeași prezentare arată la fel
        // de fiecare dată, ceea ce contează când demonstrezi ceva în fața cuiva.
        var random = new Random(seed ?? 20260817);
        var created = 0;

        for (var i = 0; i < customerCount; i++)
        {
            var customer = CreateCustomer(random, i);
            var result = _loyalty.EnrollCustomer(customer, actor);

            if (result.Failed)
                continue;

            created++;
            GenerateHistory(result.Value!, random, actor);
        }

        // Punctele acordate cu mai bine de un an în urmă trebuie să apară expirate,
        // altfel soldurile ar fi nerealist de mari.
        _loyalty.RunExpiry(actor);

        return OperationResult<int>.Ok(created);
    }

    private Customer CreateCustomer(Random random, int index)
    {
        var isFemale = random.Next(100) < 70; // profilul unui magazin de haine
        var firstName = isFemale
            ? FemaleNames[random.Next(FemaleNames.Length)]
            : MaleNames[random.Next(MaleNames.Length)];

        var lastName = LastNames[random.Next(LastNames.Length)];

        // Înscrieri răspândite pe ultimii doi ani.
        var enrolledDaysAgo = random.Next(20, 730);

        return new Customer
        {
            FirstName = firstName,
            LastName = lastName,
            Phone = $"07{random.Next(20, 80):00}{random.Next(100000, 999999)}",
            Email = random.Next(100) < 60
                ? $"{Transliterate(firstName)}.{Transliterate(lastName)}{index}@exemplu.ro"
                : null,
            BirthDate = new DateTime(random.Next(1965, 2007), random.Next(1, 13), random.Next(1, 28)),
            MarketingConsent = random.Next(100) < 65,
            EnrolledAt = _clock.Now.AddDays(-enrolledDaysAgo),
        };
    }

    /// <summary>
    /// Generează cumpărăturile unui client. Clienții nu cumpără la fel: majoritatea
    /// vin rar, o minoritate vine des și cheltuie mult. Fără această diferență,
    /// toți ar ajunge pe același nivel și programul nu ar demonstra nimic.
    /// </summary>
    private void GenerateHistory(Customer customer, Random random, AppUser actor)
    {
        var profile = random.Next(100);

        var (purchaseCount, minAmount, maxAmount) = profile switch
        {
            < 45 => (random.Next(1, 4), 80m, 250m),      // ocazional
            < 80 => (random.Next(3, 8), 150m, 450m),     // obișnuit
            _ => (random.Next(8, 18), 250m, 900m),       // fidel
        };

        var enrolledDaysAgo = (int)(_clock.Now - customer.EnrolledAt).TotalDays;
        if (enrolledDaysAgo < 1)
            enrolledDaysAgo = 1;

        for (var i = 0; i < purchaseCount; i++)
        {
            var daysAgo = random.Next(0, enrolledDaysAgo);
            var occurredAt = _clock.Now.AddDays(-daysAgo)
                .Date
                .AddHours(random.Next(10, 20))
                .AddMinutes(random.Next(0, 60));

            if (occurredAt > _clock.Now)
                occurredAt = _clock.Now.AddMinutes(-5);

            // Sume care se termină în ,90 sau ,99, ca prețurile reale de magazin.
            var whole = random.Next((int)minAmount, (int)maxAmount);
            var amount = whole + (random.Next(2) == 0 ? 0.90m : 0.99m);

            _loyalty.RecordPurchase(new PurchaseRequest
            {
                CustomerId = customer.Id,
                GrossAmount = amount,
                OccurredAt = occurredAt,
                ReceiptNumber = $"D{occurredAt:yyMMdd}-{random.Next(1000, 9999)}",
            }, actor);
        }
    }

    /// <summary>Nume fără diacritice, pentru adresele de e-mail generate.</summary>
    private static string Transliterate(string value) =>
        value.ToLowerInvariant()
             .Replace("ă", "a").Replace("â", "a").Replace("î", "i")
             .Replace("ș", "s").Replace("ț", "t");
}
