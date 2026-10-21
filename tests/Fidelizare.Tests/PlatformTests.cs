using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>
/// Capabilitățile care fac programul configurabil de către magazin: baza de calcul
/// a nivelului, campaniile promoționale, câmpurile proprii și formatul codului de card.
/// </summary>
public class PlatformTests
{
    private static void ReplaceTiers(TestEnvironment env, params (string Name, decimal Threshold, decimal Multiplier)[] tiers)
    {
        using var uow = env.Factory.Create();

        foreach (var existing in uow.Tiers.GetAll())
            uow.Tiers.Delete(existing.Id);

        var order = 1;
        foreach (var (name, threshold, multiplier) in tiers)
        {
            uow.Tiers.Insert(new Tier
            {
                Name = name,
                Threshold = threshold,
                EarnMultiplier = multiplier,
                SortOrder = order++,
            });
        }

        uow.Commit();
    }

    // ================================================= Baza de calcul a nivelului

    [Fact]
    public void Nivelul_se_poate_calcula_dupa_punctele_acumulate()
    {
        using var env = new TestEnvironment();

        // Praguri exprimate în puncte, nu în lei.
        ReplaceTiers(env, ("Start", 0m, 1m), ("Avansat", 200m, 1.5m));
        env.SetRules(r => r.TierBasis = TierBasis.PointsEarned);

        var customer = env.Enroll();

        // Bonusul de bun venit aduce 50 de puncte, cumpărarea încă 100: total 150.
        env.Buy(customer.Id, 1000m);
        Assert.Equal("Start", env.Loyalty.GetSummary(customer.Id)!.Tier.Name);

        // Încă 60 de puncte trec pragul de 200.
        env.Buy(customer.Id, 600m);

        var summary = env.Loyalty.GetSummary(customer.Id)!;
        Assert.Equal("Avansat", summary.Tier.Name);
        Assert.Equal(210m, summary.QualifyingValue);
    }

    [Fact]
    public void Punctele_cheltuite_nu_coboara_clientul_din_nivel()
    {
        using var env = new TestEnvironment();

        ReplaceTiers(env, ("Start", 0m, 1m), ("Avansat", 200m, 1.5m));
        env.SetRules(r => r.TierBasis = TierBasis.PointsEarned);

        var customer = env.Enroll();
        env.Buy(customer.Id, 2000m);
        Assert.Equal("Avansat", env.Loyalty.GetSummary(customer.Id)!.Tier.Name);

        // Clientul își folosește punctele pe o recompensă.
        Reward reward;
        using (var uow = env.Factory.Create())
            reward = uow.Rewards.GetAll().First(r => r.PointsCost == 100);

        var redeem = env.Loyalty.RedeemReward(new RedeemRequest
        {
            CustomerId = customer.Id,
            RewardId = reward.Id,
            BasketAmount = 200m,
        }, env.Cashier);

        Assert.True(redeem.Success, redeem.Error);

        // Nivelul reflectă cât a acumulat, nu cât i-a mai rămas: altfel clientul ar
        // fi pedepsit tocmai pentru că a folosit programul.
        Assert.Equal("Avansat", env.Loyalty.GetSummary(customer.Id)!.Tier.Name);
    }

    [Fact]
    public void Nivelul_se_poate_calcula_dupa_numarul_de_cumparari()
    {
        using var env = new TestEnvironment();

        ReplaceTiers(env, ("Ocazional", 0m, 1m), ("Fidel", 5m, 1.4m));
        env.SetRules(r => r.TierBasis = TierBasis.PurchaseCount);

        var customer = env.Enroll();

        for (var i = 0; i < 4; i++)
            env.Buy(customer.Id, 100m);

        Assert.Equal("Ocazional", env.Loyalty.GetSummary(customer.Id)!.Tier.Name);

        env.Buy(customer.Id, 100m);

        var summary = env.Loyalty.GetSummary(customer.Id)!;
        Assert.Equal("Fidel", summary.Tier.Name);
        Assert.Equal(5m, summary.QualifyingValue);
    }

    // ================================================================= Campanii

    private static int AddCampaign(TestEnvironment env, string name, DateTime from, DateTime to,
        decimal multiplier, decimal minPurchase = 0m, int daysMask = 0)
    {
        using var uow = env.Factory.Create();

        var id = uow.Campaigns.Insert(new Campaign
        {
            Name = name,
            StartsOn = from,
            EndsOn = to,
            Multiplier = multiplier,
            MinPurchaseAmount = minPurchase,
            DaysOfWeekMask = daysMask,
            IsActive = true,
        });

        uow.Commit();
        return id;
    }

    [Fact]
    public void Campania_dubleaza_punctele_in_perioada_ei()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        AddCampaign(env, "Puncte duble", env.Clock.Today.AddDays(-1), env.Clock.Today.AddDays(1), 2m);

        var purchase = env.Buy(customer.Id, 500m);

        Assert.Equal(100, purchase.PointsEarned);          // 50 de bază, dublate
        Assert.Equal("Puncte duble", purchase.CampaignName);
        Assert.Equal(2m, purchase.CampaignMultiplier);
    }

    [Fact]
    public void Campania_nu_se_aplica_in_afara_perioadei()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        AddCampaign(env, "Trecută", env.Clock.Today.AddDays(-30), env.Clock.Today.AddDays(-10), 3m);

        var purchase = env.Buy(customer.Id, 500m);

        Assert.Equal(50, purchase.PointsEarned);
        Assert.Null(purchase.CampaignName);
        Assert.Equal(1m, purchase.CampaignMultiplier);
    }

    [Fact]
    public void Campania_se_combina_cu_multiplicatorul_de_nivel()
    {
        using var env = new TestEnvironment();

        ReplaceTiers(env, ("Baza", 0m, 1m), ("Gold", 1000m, 1.5m));
        var customer = env.Enroll();

        env.Buy(customer.Id, 1000m);   // urcă pe Gold
        AddCampaign(env, "Weekend", env.Clock.Today, env.Clock.Today, 2m);

        var purchase = env.Buy(customer.Id, 200m);

        // 20 de puncte de bază, ×1,5 nivel, ×2 campanie = 60.
        Assert.Equal(60, purchase.PointsEarned);
        Assert.Equal(1.5m, purchase.MultiplierApplied);
        Assert.Equal(2m, purchase.CampaignMultiplier);
    }

    [Fact]
    public void La_campanii_suprapuse_se_aplica_cea_mai_generoasa_nu_produsul_lor()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        AddCampaign(env, "Dubla", env.Clock.Today, env.Clock.Today, 2m);
        AddCampaign(env, "Tripla", env.Clock.Today, env.Clock.Today, 3m);

        var purchase = env.Buy(customer.Id, 100m);

        // 10 puncte de bază: ×3, nu ×6. Produsul celor două ar fi o generozitate
        // pe care nimeni nu a decis-o.
        Assert.Equal(30, purchase.PointsEarned);
        Assert.Equal("Tripla", purchase.CampaignName);
    }

    [Fact]
    public void Campania_respecta_pragul_minim_al_bonului()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        AddCampaign(env, "Peste 300 lei", env.Clock.Today, env.Clock.Today, 2m, minPurchase: 300m);

        Assert.Equal(20, env.Buy(customer.Id, 200m).PointsEarned);   // sub prag, fără campanie
        Assert.Equal(60, env.Buy(customer.Id, 300m).PointsEarned);   // exact la prag, dublat
    }

    [Fact]
    public void Campania_se_poate_limita_la_anumite_zile_ale_saptamanii()
    {
        // 10 martie 2026 este o zi de marți.
        using var env = new TestEnvironment(new DateTime(2026, 3, 10, 11, 0, 0));
        var customer = env.Enroll();

        var doarLuni = 1 << Campaign.BitFor(DayOfWeek.Monday);
        AddCampaign(env, "Doar lunea", env.Clock.Today.AddDays(-7), env.Clock.Today.AddDays(7), 2m, daysMask: doarLuni);

        Assert.Equal(20, env.Buy(customer.Id, 200m).PointsEarned);

        var doarMarti = 1 << Campaign.BitFor(DayOfWeek.Tuesday);
        AddCampaign(env, "Doar marțea", env.Clock.Today.AddDays(-7), env.Clock.Today.AddDays(7), 2m, daysMask: doarMarti);

        Assert.Equal(40, env.Buy(customer.Id, 200m).PointsEarned);
    }

    // =========================================================== Câmpuri proprii

    private static int AddField(TestEnvironment env, string label, CustomFieldKind kind = CustomFieldKind.Text,
        bool required = false, string? options = null)
    {
        using var uow = env.Factory.Create();

        var id = uow.CustomFields.InsertDefinition(new CustomFieldDefinition
        {
            Label = label,
            Key = CustomFieldDefinition.SuggestKey(label),
            Kind = kind,
            IsRequired = required,
            Options = options,
            IncludeInSearch = true,
            IsActive = true,
        });

        uow.Commit();
        return id;
    }

    [Fact]
    public void Magazinul_isi_poate_adauga_campuri_proprii_pe_fisa_clientului()
    {
        using var env = new TestEnvironment();

        var marime = AddField(env, "Mărime preferată");
        var oras = AddField(env, "Oraș");

        var result = env.Loyalty.EnrollCustomer(new Customer
        {
            LastName = "Ionescu",
            FirstName = "Ana",
            CustomValues = { [marime] = "M", [oras] = "Cluj-Napoca" },
        }, env.Admin);

        Assert.True(result.Success, result.Error);

        var saved = env.Loyalty.GetCustomer(result.Value!.Id)!;
        Assert.Equal("M", saved.CustomValues[marime]);
        Assert.Equal("Cluj-Napoca", saved.CustomValues[oras]);
    }

    [Fact]
    public void Un_camp_obligatoriu_opreste_salvarea_daca_este_gol()
    {
        using var env = new TestEnvironment();
        AddField(env, "Oraș", required: true);

        var result = env.Loyalty.EnrollCustomer(
            new Customer { LastName = "Ionescu", FirstName = "Ana" }, env.Admin);

        Assert.False(result.Success);
        Assert.Contains("Oraș", result.Error!);
    }

    [Fact]
    public void Un_camp_de_tip_lista_accepta_doar_optiunile_definite()
    {
        using var env = new TestEnvironment();
        var stil = AddField(env, "Stil", CustomFieldKind.Choice, options: "Casual\nElegant\nSport");

        var gresit = env.Loyalty.EnrollCustomer(new Customer
        {
            LastName = "Ionescu",
            FirstName = "Ana",
            CustomValues = { [stil] = "Altceva" },
        }, env.Admin);

        Assert.False(gresit.Success);

        var corect = env.Loyalty.EnrollCustomer(new Customer
        {
            LastName = "Popescu",
            FirstName = "Dan",
            CustomValues = { [stil] = "Elegant" },
        }, env.Admin);

        Assert.True(corect.Success, corect.Error);
    }

    [Fact]
    public void Cautarea_gaseste_clientul_dupa_valoarea_unui_camp_propriu()
    {
        using var env = new TestEnvironment();
        var oras = AddField(env, "Oraș");

        env.Loyalty.EnrollCustomer(new Customer
        {
            LastName = "Ionescu",
            FirstName = "Ana",
            CustomValues = { [oras] = "Târgu Mureș" },
        }, env.Admin);

        env.Loyalty.EnrollCustomer(new Customer { LastName = "Popescu", FirstName = "Dan" }, env.Admin);

        // Fără diacritice, ca la casă.
        var found = env.Loyalty.SearchCustomers(new CustomerQuery { SearchText = "targu mures" });

        Assert.Single(found);
        Assert.Equal("Ionescu", found[0].LastName);
    }

    [Fact]
    public void Stergerea_unui_camp_nu_impiedica_salvarea_clientilor()
    {
        using var env = new TestEnvironment();
        var camp = AddField(env, "Temporar");

        var customer = env.Loyalty.EnrollCustomer(new Customer
        {
            LastName = "Ionescu",
            FirstName = "Ana",
            CustomValues = { [camp] = "ceva" },
        }, env.Admin).Value!;

        using (var uow = env.Factory.Create())
        {
            uow.CustomFields.DeleteDefinition(camp);
            uow.Commit();
        }

        var reloaded = env.Loyalty.GetCustomer(customer.Id)!;
        reloaded.LastName = "Ionescu-Popa";

        var result = env.Loyalty.UpdateCustomer(reloaded, env.Admin);
        Assert.True(result.Success, result.Error);
    }

    // ====================================================== Format cod de card

    [Fact]
    public void Formatul_codului_de_card_este_configurabil()
    {
        using var env = new TestEnvironment();

        env.SetRules(r =>
        {
            r.CardCodePrefix = "MAG-";
            r.CardCodeDigits = 4;
        });

        var customer = env.Enroll();

        Assert.StartsWith("MAG-", customer.CardCode);
        Assert.Matches(@"^MAG-\d{4}$", customer.CardCode);
    }

    [Fact]
    public void Codurile_generate_raman_consecutive()
    {
        using var env = new TestEnvironment();
        env.SetRules(r => r.CardCodeDigits = 4);

        var primul = env.Enroll("Unu", "Client", "0700000001").CardCode;
        var alDoilea = env.Enroll("Doi", "Client", "0700000002").CardCode;

        Assert.Equal(int.Parse(primul) + 1, int.Parse(alDoilea));
    }
}
