using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>Retururile parțiale: punctele, rulajul și starea bonului după ce clientul aduce produse înapoi.</summary>
public class ReturnTests
{
    [Fact]
    public void Returul_partial_retrage_punctele_proportional()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 1000m); // 100 puncte, total 150

        var result = Return(env, purchase.Id, 400m);

        Assert.True(result.Success, result.Error);
        Assert.Equal(40, result.Value!.Return.PointsReversed);
        Assert.False(result.Value.FullyReturned);
        Assert.Equal(110, env.Balance(customer.Id));

        var stored = env.Loyalty.GetPurchase(purchase.Id)!;
        Assert.Equal(400m, stored.ReturnedAmount);
        Assert.Equal(600m, stored.RemainingAmount);
        Assert.Equal(40, stored.PointsReturned);
        Assert.False(stored.IsVoided);
    }

    [Fact]
    public void Retururile_succesive_retrag_impreuna_exact_punctele_acordate()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 97m); // 9 puncte

        // Rămân 50 lei din 97: clientul păstrează 4 puncte, deci se retrag 5.
        var first = Return(env, purchase.Id, 47m);
        Assert.True(first.Success, first.Error);
        Assert.Equal(5, first.Value!.Return.PointsReversed);

        var second = Return(env, purchase.Id, 50m);
        Assert.True(second.Success, second.Error);
        Assert.Equal(4, second.Value!.Return.PointsReversed);
        Assert.True(second.Value.FullyReturned);

        Assert.Equal(50, env.Balance(customer.Id));

        var stored = env.Loyalty.GetPurchase(purchase.Id)!;
        Assert.True(stored.IsVoided);
        Assert.Equal(9, stored.PointsReturned);
        Assert.Equal(2, env.Loyalty.GetReturns(purchase.Id).Count);
    }

    [Fact]
    public void Returul_nu_poate_depasi_ce_a_ramas_din_bon()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 200m);

        Assert.True(Return(env, purchase.Id, 150m).Success);

        var result = Return(env, purchase.Id, 60m);

        Assert.False(result.Success);
        Assert.Contains("50", result.Error!);
    }

    [Fact]
    public void Suma_returnata_iese_din_rulajul_pentru_nivel()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 1000m);

        Assert.Equal("Silver", env.Loyalty.GetSummary(customer.Id)!.Tier.Name);

        Assert.True(Return(env, purchase.Id, 100m).Success);

        var summary = env.Loyalty.GetSummary(customer.Id)!;
        Assert.Equal(900m, summary.QualifyingValue);
        Assert.Equal(900m, summary.LifetimeSpend);
        Assert.Equal("Bronze", summary.Tier.Name);
    }

    [Fact]
    public void Anularea_dupa_un_retur_partial_retrage_doar_restul_punctelor()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 1000m);

        Assert.True(Return(env, purchase.Id, 400m).Success);
        Assert.Equal(110, env.Balance(customer.Id));

        var result = env.Loyalty.VoidPurchase(purchase.Id, "Bon introdus greșit", env.Admin);

        Assert.True(result.Success, result.Error);
        Assert.Equal(50, env.Balance(customer.Id));
    }

    [Fact]
    public void Returul_cu_punctele_deja_cheltuite_nu_lasa_sold_negativ()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 1000m); // 100 puncte, total 150

        var reward = RewardCosting(env, 100);
        var redeem = env.Loyalty.RedeemReward(new RedeemRequest
        {
            CustomerId = customer.Id,
            RewardId = reward.Id,
            BasketAmount = 200m,
        }, env.Cashier);
        Assert.True(redeem.Success, redeem.Error);
        Assert.Equal(50, env.Balance(customer.Id));

        // Returul întregului bon cere 100 de puncte, dar mai sunt doar 50.
        var result = Return(env, purchase.Id, 1000m);

        Assert.True(result.Success, result.Error);
        Assert.Equal(100, result.Value!.Return.PointsReversed);
        Assert.Equal(50, result.Value.Return.PointsShortfall);
        Assert.Equal(0, env.Balance(customer.Id));
    }

    [Fact]
    public void Casierul_poate_face_retur_si_ramane_trecut_ca_operator()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 100m); // 10 puncte

        var result = env.Loyalty.ReturnPurchase(new ReturnRequest
        {
            PurchaseId = purchase.Id,
            Amount = 50m,
            Reason = "Mărime greșită",
        }, env.Cashier);

        Assert.True(result.Success, result.Error);
        Assert.Equal(55, env.Balance(customer.Id));

        var stored = Assert.Single(env.Loyalty.GetReturns(purchase.Id));
        Assert.Equal(env.Cashier.Id, stored.UserId);
    }

    [Fact]
    public void Returul_pe_o_vanzare_anulata_este_refuzat()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 100m);

        Assert.True(env.Loyalty.VoidPurchase(purchase.Id, "Storno", env.Admin).Success);

        Assert.False(Return(env, purchase.Id, 50m).Success);
    }

    [Fact]
    public void Bonul_se_gaseste_dupa_numar_chiar_daca_numarul_a_fost_refolosit()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        var wrong = env.Buy(customer.Id, 100m, receipt: "BON-7");
        Assert.True(env.Loyalty.VoidPurchase(wrong.Id, "Sumă greșită", env.Admin).Success);
        var right = env.Buy(customer.Id, 120m, receipt: "BON-7");

        var found = env.Loyalty.FindPurchaseByReceipt(" BON-7 ");

        Assert.NotNull(found);
        Assert.Equal(right.Id, found.Id);
        Assert.Null(env.Loyalty.FindPurchaseByReceipt("BON-8"));
    }

    [Fact]
    public void Returul_apare_in_istoricul_punctelor()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        var purchase = env.Buy(customer.Id, 500m);

        Assert.True(Return(env, purchase.Id, 200m, "Pantaloni, mărime greșită").Success);

        using var uow = env.Factory.Create();
        var line = uow.Points.GetTransactions(customer.Id).First(t => t.Type == PointTransactionType.Return);

        Assert.Equal(-20, line.Points);
        Assert.Contains("Pantaloni", line.Note);
    }

    [Theory]
    [InlineData(100, 1000, 0, 400, 40)]   // 600 rămași din 1000: se păstrează 60
    [InlineData(9, 97, 0, 47, 5)]         // 50 din 97: se păstrează 4
    [InlineData(9, 97, 47, 50, 4)]        // restul bonului: se retrage tot ce a rămas
    [InlineData(0, 30, 0, 10, 0)]         // bon fără puncte
    public void Punctele_de_retras_la_retur(int earned, int net, int alreadyReturned, int amount, int expected)
    {
        var purchase = new Purchase
        {
            NetAmount = net,
            PointsEarned = earned,
            ReturnedAmount = alreadyReturned,
            PointsReturned = alreadyReturned == 0 ? 0 : PointsCalculator.PointsToReverseForReturn(
                new Purchase { NetAmount = net, PointsEarned = earned }, alreadyReturned),
        };

        Assert.Equal(expected, PointsCalculator.PointsToReverseForReturn(purchase, amount));
    }

    private static OperationResult<ReturnOutcome> Return(
        TestEnvironment env, int purchaseId, decimal amount, string reason = "Produs returnat") =>
        env.Loyalty.ReturnPurchase(new ReturnRequest
        {
            PurchaseId = purchaseId,
            Amount = amount,
            Reason = reason,
        }, env.Admin);

    private static Reward RewardCosting(TestEnvironment env, int points)
    {
        using var uow = env.Factory.Create();
        return uow.Rewards.GetAll().First(r => r.PointsCost == points);
    }
}
