using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>Comportamentul complet, cu baza de date reală în spate.</summary>
public class LoyaltyServiceTests
{
    [Fact]
    public void Inscrierea_acorda_bonusul_de_bun_venit()
    {
        using var env = new TestEnvironment();

        var customer = env.Enroll();

        Assert.Equal(50, env.Balance(customer.Id));
        Assert.Matches(@"^\d{6}$", customer.CardCode);
    }

    [Fact]
    public void Cumpararea_acorda_puncte_si_pastreaza_nivelul_din_momentul_vanzarii()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        var purchase = env.Buy(customer.Id, 250m);

        Assert.Equal(25, purchase.PointsEarned);
        Assert.Equal("Bronze", purchase.TierAtPurchase);
        Assert.Equal(75, env.Balance(customer.Id)); // 50 bun venit + 25
    }

    [Fact]
    public void Rulajul_ridica_nivelul_iar_multiplicatorul_se_aplica_de_la_vanzarea_urmatoare()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        // Ajunge exact pe pragul Silver.
        env.Buy(customer.Id, 1000m);

        var summary = env.Loyalty.GetSummary(customer.Id)!;
        Assert.Equal("Silver", summary.Tier.Name);

        // Vânzarea următoare beneficiază de multiplicatorul 1,25.
        var second = env.Buy(customer.Id, 100m);
        Assert.Equal(12, second.PointsEarned);
    }

    [Fact]
    public void Telefonul_duplicat_este_respins()
    {
        using var env = new TestEnvironment();
        env.Enroll(phone: "0722123456");

        var result = env.Loyalty.EnrollCustomer(
            new Customer { LastName = "Ionescu", FirstName = "Vlad", Phone = "0722 123 456" }, env.Admin);

        Assert.False(result.Success);
        Assert.Contains("telefon", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Acelasi_bon_nu_poate_fi_inregistrat_de_doua_ori()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        env.Buy(customer.Id, 100m, receipt: "BON-001");

        var result = env.Loyalty.RecordPurchase(new PurchaseRequest
        {
            CustomerId = customer.Id,
            GrossAmount = 100m,
            ReceiptNumber = "BON-001",
        }, env.Cashier);

        Assert.False(result.Success);
        Assert.Contains("BON-001", result.Error!);
    }

    // ------------------------------------------------------------- Expirare

    [Fact]
    public void Punctele_expira_dupa_perioada_de_valabilitate()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        env.Buy(customer.Id, 1000m);

        Assert.Equal(150, env.Balance(customer.Id)); // 50 + 100

        env.Clock.AdvanceMonths(13);
        var expired = env.Loyalty.RunExpiry(env.Admin);

        Assert.Equal(150, expired);
        Assert.Equal(0, env.Balance(customer.Id));
    }

    [Fact]
    public void Se_consuma_intai_punctele_care_expira_cel_mai_devreme()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        // Sumele sunt alese ca să nu ridice clientul pe alt nivel, altfel ar
        // interveni multiplicatorul și testul nu ar mai fi despre ordinea consumului.
        env.Buy(customer.Id, 500m);           // 50 puncte, expiră peste 12 luni
        env.Clock.AdvanceMonths(6);
        env.Buy(customer.Id, 900m);           // 90 puncte, expiră cu 6 luni mai târziu

        Assert.Equal(190, env.Balance(customer.Id)); // 50 bun venit + 50 + 90

        // Se revendică o recompensă de 100 de puncte.
        var reward = FirstRewardCosting(env, 100);
        var redeem = env.Loyalty.RedeemReward(new RedeemRequest
        {
            CustomerId = customer.Id,
            RewardId = reward.Id,
            BasketAmount = 200m,
        }, env.Cashier);

        Assert.True(redeem.Success, redeem.Error);

        Assert.Equal(90, env.Balance(customer.Id));

        // Se consumă în ordinea expirării: bonusul de bun venit (50) și primul lot
        // de cumpărare (50) se golesc exact, iar lotul cel mai nou rămâne neatins.
        using var uow = env.Factory.Create();
        var lots = uow.Points.GetOpenLots(customer.Id);

        Assert.Equal([90], lots.Select(l => l.PointsRemaining).ToArray());
    }

    [Fact]
    public void Punctele_expirate_nu_mai_pot_fi_folosite()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        env.Buy(customer.Id, 5000m);

        env.Clock.AdvanceMonths(13);

        var reward = FirstRewardCosting(env, 100);
        var result = env.Loyalty.RedeemReward(new RedeemRequest
        {
            CustomerId = customer.Id,
            RewardId = reward.Id,
            BasketAmount = 200m,
        }, env.Cashier);

        Assert.False(result.Success);
    }

    // ------------------------------------------------------------- Anulări

    [Fact]
    public void Anularea_unei_vanzari_retrage_punctele_acordate()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 1000m);

        Assert.Equal(150, env.Balance(customer.Id));

        var result = env.Loyalty.VoidPurchase(purchase.Id, "Client a returnat produsul", env.Admin);

        Assert.True(result.Success, result.Error);
        Assert.Equal(50, env.Balance(customer.Id));
    }

    [Fact]
    public void Anularea_unei_vanzari_al_carei_puncte_au_fost_cheltuite_nu_lasa_sold_negativ()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 1000m); // 100 puncte, total 150

        var reward = FirstRewardCosting(env, 100);
        env.Loyalty.RedeemReward(new RedeemRequest
        {
            CustomerId = customer.Id,
            RewardId = reward.Id,
            BasketAmount = 200m,
        }, env.Cashier);

        Assert.Equal(50, env.Balance(customer.Id));

        // Se retrag cele 100 de puncte, dar mai există doar 50.
        var result = env.Loyalty.VoidPurchase(purchase.Id, "Storno", env.Admin);

        Assert.False(result.Success);          // avertizare, nu eșec al operațiunii
        Assert.Contains("consumate", result.Error!);
        Assert.Equal(0, env.Balance(customer.Id));

        using var uow = env.Factory.Create();
        Assert.True(uow.Purchases.GetById(purchase.Id)!.IsVoided);
    }

    [Fact]
    public void Anularea_unei_recompense_restituie_punctele_in_loturile_originale()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        env.Buy(customer.Id, 1000m);

        var reward = FirstRewardCosting(env, 100);
        var redeem = env.Loyalty.RedeemReward(new RedeemRequest
        {
            CustomerId = customer.Id,
            RewardId = reward.Id,
            BasketAmount = 200m,
        }, env.Cashier);

        Assert.True(redeem.Success, redeem.Error);
        Assert.Equal(50, env.Balance(customer.Id));

        var result = env.Loyalty.VoidRedemption(redeem.Value!.Redemption.Id, "Recompensă acordată greșit", env.Admin);

        Assert.True(result.Success, result.Error);
        Assert.Equal(150, env.Balance(customer.Id));
    }

    [Fact]
    public void Casierul_nu_poate_anula_vanzari()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 100m);

        var result = env.Loyalty.VoidPurchase(purchase.Id, "test", env.Cashier);

        Assert.False(result.Success);
        Assert.Contains("responsabil", result.Error!);
    }

    // ------------------------------------------------------------- Corecții

    [Fact]
    public void Corectia_in_minus_nu_poate_depasi_soldul()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        var result = env.Loyalty.AdjustPoints(customer.Id, -100, "Test", env.Admin);

        Assert.False(result.Success);
        Assert.Contains("50", result.Error!);
    }

    [Fact]
    public void Corectia_in_plus_se_vede_in_sold_si_in_istoric()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        var result = env.Loyalty.AdjustPoints(customer.Id, 200, "Compensare reclamație", env.Admin);
        Assert.True(result.Success, result.Error);
        Assert.Equal(250, env.Balance(customer.Id));

        using var uow = env.Factory.Create();
        var history = uow.Points.GetTransactions(customer.Id);
        Assert.Contains(history, t => t.Type == PointTransactionType.AdjustmentPlus && t.Points == 200);
    }

    // -------------------------------------------------------------- Căutare

    [Fact]
    public void Cautarea_ignora_diacriticele_si_majusculele()
    {
        using var env = new TestEnvironment();
        env.Loyalty.EnrollCustomer(
            new Customer { LastName = "Țăranu", FirstName = "Gheorghiță", Phone = "0733111222" }, env.Admin);

        var found = env.Loyalty.SearchCustomers(new CustomerQuery { SearchText = "taranu gheorghita" });

        Assert.Single(found);
    }

    [Fact]
    public void Cautarea_dupa_telefon_functioneaza_cu_spatii()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll(phone: "0722123456");

        var found = env.Loyalty.FindByCardOrPhone("0722 123 456");

        Assert.NotNull(found);
        Assert.Equal(customer.Id, found.Id);
    }

    // ---------------------------------------------------------------- Bani

    [Fact]
    public void Sumele_se_pastreaza_exact_dupa_multe_vanzari()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        // 0,10 lei adunat de o mie de ori trebuie să dea exact 100 lei.
        for (var i = 0; i < 1000; i++)
            env.Buy(customer.Id, 0.10m);

        var summary = env.Loyalty.GetSummary(customer.Id)!;
        Assert.Equal(100m, summary.LifetimeSpend);
    }

    private static Reward FirstRewardCosting(TestEnvironment env, int points)
    {
        using var uow = env.Factory.Create();
        return uow.Rewards.GetAll().First(r => r.PointsCost == points);
    }
}
