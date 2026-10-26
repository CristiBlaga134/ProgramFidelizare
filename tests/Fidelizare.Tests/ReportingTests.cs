using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>Raportul de perioadă, pentru tot magazinul și pentru un singur casier.</summary>
public class ReportingTests
{
    [Fact]
    public void Raportul_unui_casier_cuprinde_doar_vanzarile_lui()
    {
        using var env = new TestEnvironment();
        var reporting = new ReportingService(env.Factory, env.Clock);
        var other = env.CreateUser("ana", "Ana Popescu", UserRole.Cashier);

        var customer = env.Enroll();
        env.Buy(customer.Id, 300m); // casierul de test

        var sale = env.Loyalty.RecordPurchase(new PurchaseRequest
        {
            CustomerId = customer.Id,
            GrossAmount = 200m,
        }, other);
        Assert.True(sale.Success, sale.Error);

        var today = env.Clock.Today;
        var store = reporting.GetPeriodReport(today, today.AddDays(1));
        var mine = reporting.GetPeriodReport(today, today.AddDays(1), env.Cashier.Id);
        var hers = reporting.GetPeriodReport(today, today.AddDays(1), other.Id);

        Assert.Equal(2, store.PurchaseCount);
        Assert.Equal(500m, store.NetSales);

        Assert.Equal(1, mine.PurchaseCount);
        Assert.Equal(300m, mine.NetSales);
        Assert.Equal(30, mine.PointsIssued);

        Assert.Equal(1, hers.PurchaseCount);
        Assert.Equal(200m, hers.NetSales);
    }

    [Fact]
    public void Clientii_noi_ai_unui_utilizator_se_numara_din_jurnal()
    {
        using var env = new TestEnvironment();
        var reporting = new ReportingService(env.Factory, env.Clock);

        env.Enroll("Popescu", "Ioana", "0722000001");
        env.Enroll("Ionescu", "Maria", "0722000002");

        var today = env.Clock.Today;

        // TestEnvironment înscrie clienții cu contul de administrator.
        Assert.Equal(2, reporting.GetPeriodReport(today, today.AddDays(1), env.Admin.Id).NewCustomers);
        Assert.Equal(0, reporting.GetPeriodReport(today, today.AddDays(1), env.Cashier.Id).NewCustomers);
    }
}
