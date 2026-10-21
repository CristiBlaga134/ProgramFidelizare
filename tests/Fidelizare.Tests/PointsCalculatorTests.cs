using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>Regulile de calcul, verificate izolat de bază de date.</summary>
public class PointsCalculatorTests
{
    private static readonly LoyaltyRules Rules = new() { LeiPerPoint = 10m };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9.99, 0)]
    [InlineData(10, 1)]
    [InlineData(97, 9)]
    [InlineData(100, 10)]
    [InlineData(249.90, 24)]
    public void Punctele_se_rotunjesc_in_jos(decimal amount, int expected) =>
        Assert.Equal(expected, PointsCalculator.PointsForPurchase(amount, 1m, Rules));

    [Fact]
    public void Multiplicatorul_de_nivel_se_aplica_dupa_rotunjirea_de_baza()
    {
        // 97 lei dau 9 puncte de bază; cu 1,25 rezultă 11,25, rotunjit tot în jos.
        Assert.Equal(11, PointsCalculator.PointsForPurchase(97m, 1.25m, Rules));
        Assert.Equal(13, PointsCalculator.PointsForPurchase(97m, 1.5m, Rules));
    }

    [Fact]
    public void Sub_pragul_minim_nu_se_acorda_puncte()
    {
        var rules = new LoyaltyRules { LeiPerPoint = 10m, MinPurchaseForPoints = 50m };

        Assert.Equal(0, PointsCalculator.PointsForPurchase(49.99m, 1m, rules));
        Assert.Equal(5, PointsCalculator.PointsForPurchase(50m, 1m, rules));
    }

    [Fact]
    public void Sumele_negative_nu_produc_puncte() =>
        Assert.Equal(0, PointsCalculator.PointsForPurchase(-100m, 1m, Rules));

    // ------------------------------------------------------------------ Niveluri

    private static List<Tier> ThreeTiers() =>
    [
        new() { Id = 1, Name = "Bronze", Threshold = 0m, EarnMultiplier = 1m, SortOrder = 1 },
        new() { Id = 2, Name = "Silver", Threshold = 1000m, EarnMultiplier = 1.25m, SortOrder = 2 },
        new() { Id = 3, Name = "Gold", Threshold = 3000m, EarnMultiplier = 1.5m, SortOrder = 3 },
    ];

    [Theory]
    [InlineData(0, "Bronze")]
    [InlineData(999.99, "Bronze")]
    [InlineData(1000, "Silver")]
    [InlineData(2999.99, "Silver")]
    [InlineData(3000, "Gold")]
    [InlineData(100000, "Gold")]
    public void Nivelul_se_alege_dupa_ultimul_prag_atins(decimal spend, string expected) =>
        Assert.Equal(expected, PointsCalculator.DetermineTier(spend, ThreeTiers()).Name);

    [Fact]
    public void Nivelul_urmator_este_null_la_varf()
    {
        var tiers = ThreeTiers();

        Assert.Equal("Silver", PointsCalculator.GetNextTier(tiers[0], tiers)?.Name);
        Assert.Null(PointsCalculator.GetNextTier(tiers[2], tiers));
    }

    // --------------------------------------------------------------- Recompense

    [Fact]
    public void Reducerea_procentuala_respecta_plafonul()
    {
        var reward = new Reward { Kind = RewardKind.PercentageDiscount, Value = 10m, MaxDiscountAmount = 100m };

        Assert.Equal(50m, PointsCalculator.CalculateRewardDiscount(reward, 500m));
        Assert.Equal(100m, PointsCalculator.CalculateRewardDiscount(reward, 2000m));
    }

    [Fact]
    public void Reducerea_nu_poate_depasi_valoarea_cosului()
    {
        var reward = new Reward { Kind = RewardKind.FixedAmountDiscount, Value = 50m };

        Assert.Equal(30m, PointsCalculator.CalculateRewardDiscount(reward, 30m));
    }

    // --------------------------------------------------------- Zi de naștere

    [Fact]
    public void Fereastra_aniversara_acopera_zilele_dinainte_si_de_dupa()
    {
        var birth = new DateTime(1990, 6, 15);

        Assert.True(PointsCalculator.IsWithinBirthdayWindow(birth, new DateTime(2026, 6, 15), 7));
        Assert.True(PointsCalculator.IsWithinBirthdayWindow(birth, new DateTime(2026, 6, 8), 7));
        Assert.True(PointsCalculator.IsWithinBirthdayWindow(birth, new DateTime(2026, 6, 22), 7));
        Assert.False(PointsCalculator.IsWithinBirthdayWindow(birth, new DateTime(2026, 6, 23), 7));
    }

    [Fact]
    public void Fereastra_aniversara_trece_corect_peste_anul_nou()
    {
        var birth = new DateTime(1990, 1, 2);

        // 29 decembrie este la patru zile de aniversarea din anul următor.
        Assert.True(PointsCalculator.IsWithinBirthdayWindow(birth, new DateTime(2026, 12, 29), 7));
    }

    [Fact]
    public void Nascutii_pe_29_februarie_sarbatoresc_pe_28_in_anii_nebisecti()
    {
        var birth = new DateTime(2000, 2, 29);

        Assert.Equal(new DateTime(2026, 2, 28), PointsCalculator.AnniversaryIn(birth, 2026));
        Assert.Equal(new DateTime(2028, 2, 29), PointsCalculator.AnniversaryIn(birth, 2028));
    }
}
