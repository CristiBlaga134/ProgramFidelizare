using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>Cererile clienților privind datele lor: exportul și anonimizarea.</summary>
public class PersonalDataTests
{
    [Fact]
    public void Anonimizarea_sterge_datele_care_identifica_clientul()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll("Țăranu", "Gheorghiță", "0722999888", new DateTime(1990, 5, 12));
        var purchase = env.Buy(customer.Id, 500m);

        var result = env.Loyalty.AnonymizeCustomer(customer.Id, "Cererea clientului din 26.10.2026", env.Admin);

        Assert.True(result.Success, result.Error);

        var stored = env.Loyalty.GetCustomer(customer.Id)!;
        Assert.True(LoyaltyService.IsAnonymized(stored));
        Assert.DoesNotContain("Țăranu", stored.FullName);
        Assert.Null(stored.Phone);
        Assert.Null(stored.Email);
        Assert.Null(stored.BirthDate);
        Assert.False(stored.IsActive);
        Assert.False(stored.MarketingConsent);

        // Cumpărătura rămâne pentru rapoarte, punctele se anulează.
        Assert.NotNull(env.Loyalty.GetPurchase(purchase.Id));
        Assert.Equal(0, env.Balance(customer.Id));
    }

    [Fact]
    public void Anonimizarea_scoate_numele_si_telefonul_din_jurnal_si_din_istoric()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll("Țăranu", "Gheorghiță", "0722999888");
        env.Buy(customer.Id, 300m);
        Assert.True(env.Loyalty.AdjustPoints(customer.Id, 10, "Gest de bunăvoință pentru Țăranu Gheorghiță", env.Admin).Success);

        Assert.True(env.Loyalty.AnonymizeCustomer(customer.Id, "Cerere scrisă", env.Admin).Success);

        using var uow = env.Factory.Create();
        var audit = uow.Audit.GetRecent(1000);
        Assert.DoesNotContain(audit, a => (a.Details ?? "").Contains("Țăranu"));
        Assert.DoesNotContain(audit, a => (a.Details ?? "").Contains("0722999888"));
        Assert.Contains(audit, a => a.Action == "CustomerAnonymized" && a.EntityId == customer.Id);

        var notes = uow.Points.GetTransactions(customer.Id).Select(t => t.Note ?? "").ToList();
        Assert.DoesNotContain(notes, n => n.Contains("Țăranu"));
    }

    [Fact]
    public void Doar_administratorul_poate_anonimiza_si_doar_o_data()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var manager = env.CreateUser("resp", "Responsabil de test", UserRole.Manager);

        var byCashier = env.Loyalty.AnonymizeCustomer(customer.Id, "Cerere", env.Cashier);
        var byManager = env.Loyalty.AnonymizeCustomer(customer.Id, "Cerere", manager);
        Assert.False(byCashier.Success);
        Assert.False(byManager.Success);

        Assert.True(env.Loyalty.AnonymizeCustomer(customer.Id, "Cerere", env.Admin).Success);

        var again = env.Loyalty.AnonymizeCustomer(customer.Id, "Cerere", env.Admin);
        Assert.False(again.Success);
        Assert.Contains("deja", again.Error!);
    }

    [Fact]
    public void Dupa_anonimizare_telefonul_si_cardul_pot_fi_folosite_de_un_client_nou()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll(phone: "0722111222");
        var card = customer.CardCode;

        Assert.True(env.Loyalty.AnonymizeCustomer(customer.Id, "Cerere", env.Admin).Success);

        var again = env.Loyalty.EnrollCustomer(new Customer
        {
            LastName = "Ionescu",
            FirstName = "Maria",
            Phone = "0722111222",
            CardCode = card,
        }, env.Admin);

        Assert.True(again.Success, again.Error);
    }

    [Fact]
    public void Exportul_datelor_cuprinde_datele_personale_cumparaturile_si_istoricul()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll("Popescu", "Ioana", "0722123456", new DateTime(1988, 3, 4));
        env.Buy(customer.Id, 250m, receipt: "BON-42");

        var path = env.TempFile("date-client.txt");
        var result = env.Transfer.ExportCustomerData(customer.Id, path);

        Assert.True(result.Success, result.Error);

        var text = File.ReadAllText(path);
        Assert.Contains("Popescu", text);
        Assert.Contains("0722123456", text);
        Assert.Contains("04.03.1988", text);
        Assert.Contains("BON-42", text);
        Assert.Contains("Bonus de înscriere", text);
        Assert.Contains("Sold actual", text);
    }
}
