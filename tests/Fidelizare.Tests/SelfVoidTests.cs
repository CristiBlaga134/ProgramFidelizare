using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>
/// Corectura pe loc a casierului: dreptul de a-și anula singur o recompensă
/// tocmai acordată, într-un interval scurt.
///
/// Regula caută un echilibru. Fără ea, o recompensă aleasă greșit din listă
/// blochează casa până vine responsabilul. Cu ea nelimitată, anularea ar deveni
/// o cale de a umbla la solduri fără nicio supraveghere.
/// </summary>
public class SelfVoidTests
{
    private static (TestEnvironment Env, Redemption Redemption) Setup(TestEnvironment env)
    {
        var customer = env.Enroll();
        env.Buy(customer.Id, 2000m);

        Reward reward;
        using (var uow = env.Factory.Create())
            reward = uow.Rewards.GetAll().First(r => r.PointsCost == 100);

        var result = env.Loyalty.RedeemReward(new RedeemRequest
        {
            CustomerId = customer.Id,
            RewardId = reward.Id,
            BasketAmount = 200m,
        }, env.Cashier);

        Assert.True(result.Success, result.Error);
        return (env, result.Value!.Redemption);
    }

    [Fact]
    public void Casierul_isi_poate_anula_propria_recompensa_imediat()
    {
        using var env = new TestEnvironment();
        var (_, redemption) = Setup(env);

        var soldInainte = env.Balance(redemption.CustomerId);

        env.Clock.Advance(TimeSpan.FromMinutes(3));
        var result = env.Loyalty.VoidRedemption(redemption.Id, "Recompensă greșită", env.Cashier);

        Assert.True(result.Success, result.Error);
        Assert.Equal(soldInainte + redemption.PointsSpent, env.Balance(redemption.CustomerId));
    }

    [Fact]
    public void Dupa_expirarea_intervalului_este_nevoie_de_responsabil()
    {
        using var env = new TestEnvironment();
        var (_, redemption) = Setup(env);

        env.Clock.Advance(TimeSpan.FromMinutes(11));
        var result = env.Loyalty.VoidRedemption(redemption.Id, "Prea târziu", env.Cashier);

        Assert.False(result.Success);
        Assert.Contains("responsabil", result.Error!);

        // Responsabilul poate în continuare, oricând.
        var alResponsabilului = env.Loyalty.VoidRedemption(redemption.Id, "Corecție", env.Admin);
        Assert.True(alResponsabilului.Success, alResponsabilului.Error);
    }

    [Fact]
    public void Casierul_nu_poate_anula_recompensa_altcuiva()
    {
        using var env = new TestEnvironment();
        var (_, redemption) = Setup(env);

        var altCasier = env.CreateUser("casier2", "Al doilea casier", UserRole.Cashier);
        var result = env.Loyalty.VoidRedemption(redemption.Id, "Nu e a mea", altCasier);

        Assert.False(result.Success);
        Assert.Contains("acordate de tine", result.Error!);
    }

    [Fact]
    public void Intervalul_zero_opreste_complet_anularea_de_catre_casier()
    {
        using var env = new TestEnvironment();
        env.SetRules(r => r.SelfVoidMinutes = 0);

        var (_, redemption) = Setup(env);
        var result = env.Loyalty.VoidRedemption(redemption.Id, "Imediat", env.Cashier);

        Assert.False(result.Success);
        Assert.Contains("responsabil", result.Error!);
    }

    [Fact]
    public void Intervalul_este_configurabil()
    {
        using var env = new TestEnvironment();
        env.SetRules(r => r.SelfVoidMinutes = 30);

        var (_, redemption) = Setup(env);

        env.Clock.Advance(TimeSpan.FromMinutes(25));
        var result = env.Loyalty.VoidRedemption(redemption.Id, "Încă în interval", env.Cashier);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void Anularea_ramane_in_jurnalul_de_audit()
    {
        using var env = new TestEnvironment();
        var (_, redemption) = Setup(env);

        env.Loyalty.VoidRedemption(redemption.Id, "Recompensă greșită", env.Cashier);

        using var uow = env.Factory.Create();
        var entry = uow.Audit.GetRecent().First(a => a.Action == "RedemptionVoided");

        // Chiar dacă motivul este scris automat de program, autorul rămâne consemnat.
        Assert.Equal(env.Cashier.Username, entry.UserName);
        Assert.Contains("Recompensă greșită", entry.Details!);
    }
}
