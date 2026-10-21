using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>
/// Nivelurile nu sunt fixate în program. Bronze, Silver și Gold sunt doar valori
/// de pornire, pe care magazinul le poate șterge și înlocui cu ale sale.
///
/// Testele de aici pornesc de la o schemă complet diferită — cinci niveluri, cu
/// alte denumiri, alte praguri și alți multiplicatori — și verifică faptul că
/// întregul program le urmează, fără nicio referire la valorile implicite.
/// </summary>
public class TierCustomizationTests
{
    /// <summary>Înlocuiește complet nivelurile existente cu cele date.</summary>
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
                ColorHex = "#445566",
                SortOrder = order++,
            });
        }

        uow.Commit();
    }

    [Fact]
    public void Magazinul_isi_poate_defini_propriile_niveluri()
    {
        using var env = new TestEnvironment();

        ReplaceTiers(env,
            ("Membru", 0m, 1m),
            ("Argint", 500m, 1.1m),
            ("Aur", 1500m, 1.3m),
            ("Platină", 4000m, 1.6m),
            ("Diamant", 10000m, 2m));

        var tiers = env.Loyalty.GetTiers();

        Assert.Equal(5, tiers.Count);
        Assert.Equal(
            ["Membru", "Argint", "Aur", "Platină", "Diamant"],
            tiers.Select(t => t.Name).ToArray());
    }

    [Theory]
    [InlineData(0, "Membru")]
    [InlineData(499.99, "Membru")]
    [InlineData(500, "Argint")]
    [InlineData(3999, "Aur")]
    [InlineData(4000, "Platină")]
    [InlineData(25000, "Diamant")]
    public void Incadrarea_urmeaza_pragurile_definite_de_magazin(decimal spend, string asteptat)
    {
        using var env = new TestEnvironment();

        ReplaceTiers(env,
            ("Membru", 0m, 1m),
            ("Argint", 500m, 1.1m),
            ("Aur", 1500m, 1.3m),
            ("Platină", 4000m, 1.6m),
            ("Diamant", 10000m, 2m));

        Assert.Equal(asteptat, PointsCalculator.DetermineTier(spend, env.Loyalty.GetTiers()).Name);
    }

    [Fact]
    public void Vanzarea_foloseste_multiplicatorul_nivelului_definit_de_magazin()
    {
        using var env = new TestEnvironment();

        ReplaceTiers(env,
            ("Membru", 0m, 1m),
            ("Diamant", 1000m, 2m));

        var customer = env.Enroll();

        // Prima cumpărare: încă pe nivelul de bază, fără multiplicator.
        var prima = env.Buy(customer.Id, 1000m);
        Assert.Equal(100, prima.PointsEarned);
        Assert.Equal("Membru", prima.TierAtPurchase);

        // Rulajul a atins pragul, deci următoarea beneficiază de multiplicatorul 2.
        var aDoua = env.Buy(customer.Id, 300m);
        Assert.Equal(60, aDoua.PointsEarned);
        Assert.Equal("Diamant", aDoua.TierAtPurchase);
        Assert.Equal(2m, aDoua.MultiplierApplied);
    }

    [Fact]
    public void Doua_niveluri_sunt_suficiente()
    {
        using var env = new TestEnvironment();

        ReplaceTiers(env, ("Client", 0m, 1m), ("Client fidel", 2000m, 1.5m));

        var customer = env.Enroll();
        env.Buy(customer.Id, 2000m);

        var summary = env.Loyalty.GetSummary(customer.Id)!;

        Assert.Equal("Client fidel", summary.Tier.Name);
        Assert.Null(summary.NextTier);
        Assert.Equal(0m, summary.ValueToNextTier);
    }

    [Fact]
    public void Un_singur_nivel_functioneaza_ca_program_fara_niveluri()
    {
        using var env = new TestEnvironment();

        ReplaceTiers(env, ("Membru", 0m, 1m));

        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 5000m);

        var summary = env.Loyalty.GetSummary(customer.Id)!;

        Assert.Equal("Membru", summary.Tier.Name);
        Assert.Null(summary.NextTier);
        Assert.Equal(500, purchase.PointsEarned);
    }

    [Fact]
    public void Recompensa_poate_fi_rezervata_unui_nivel_propriu()
    {
        using var env = new TestEnvironment();

        ReplaceTiers(env, ("Membru", 0m, 1m), ("VIP", 3000m, 1.5m));

        int rewardId, vipId;
        using (var uow = env.Factory.Create())
        {
            vipId = uow.Tiers.GetAll().Single(t => t.Name == "VIP").Id;

            rewardId = uow.Rewards.Insert(new Reward
            {
                Name = "Reducere exclusivă VIP",
                PointsCost = 100,
                Kind = RewardKind.FixedAmountDiscount,
                Value = 50m,
                MinTierId = vipId,
                IsActive = true,
            });

            uow.Commit();
        }

        var customer = env.Enroll();
        env.Buy(customer.Id, 500m);

        // Sub prag: recompensa este blocată, cu motivul afișat la casă.
        var blocat = env.Loyalty.GetRewardsFor(customer.Id, 200m).Single(r => r.Reward.Id == rewardId);
        Assert.NotNull(blocat.Blocker);
        Assert.Contains("VIP", blocat.Blocker);

        // După ce rulajul trece pragul, aceeași recompensă devine disponibilă.
        env.Buy(customer.Id, 3000m);
        var permis = env.Loyalty.GetRewardsFor(customer.Id, 200m).Single(r => r.Reward.Id == rewardId);
        Assert.Null(permis.Blocker);
    }
}
