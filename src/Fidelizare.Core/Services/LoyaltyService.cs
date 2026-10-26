using System.Text;
using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;

namespace Fidelizare.Core.Services;

/// <summary>Datele necesare pentru a înregistra o vânzare.</summary>
public sealed class PurchaseRequest
{
    public int CustomerId { get; init; }

    /// <summary>Valoarea coșului înainte de reduceri.</summary>
    public decimal GrossAmount { get; init; }

    /// <summary>Reducerea comercială aplicată, în lei. Reducerile din puncte se adaugă separat.</summary>
    public decimal DiscountAmount { get; init; }

    public string? ReceiptNumber { get; init; }

    /// <summary>Momentul vânzării. Null înseamnă acum.</summary>
    public DateTime? OccurredAt { get; init; }
}

/// <summary>Ce s-a întâmplat efectiv la înregistrarea vânzării, pentru mesajul de la casă.</summary>
public sealed class PurchaseOutcome
{
    public required Purchase Purchase { get; init; }
    public required CustomerSummary Summary { get; init; }
    public int PointsEarned { get; init; }

    /// <summary>Completat dacă vânzarea a dus clientul pe un nivel superior.</summary>
    public Tier? PromotedToTier { get; init; }
}

/// <summary>Datele necesare pentru a acorda o recompensă.</summary>
public sealed class RedeemRequest
{
    public int CustomerId { get; init; }
    public int RewardId { get; init; }

    /// <summary>Valoarea coșului pe care se aplică reducerea.</summary>
    public decimal BasketAmount { get; init; }

    public string? ReceiptNumber { get; init; }
}

/// <summary>Datele necesare pentru returul unei părți dintr-un bon.</summary>
public sealed class ReturnRequest
{
    public int PurchaseId { get; init; }

    /// <summary>Valoarea produselor returnate, în lei.</summary>
    public decimal Amount { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public sealed class ReturnOutcome
{
    public required PurchaseReturn Return { get; init; }
    public required Purchase Purchase { get; init; }
    public required CustomerSummary Summary { get; init; }

    /// <summary>Adevărat dacă returul a epuizat bonul, care s-a închis ca anulat.</summary>
    public bool FullyReturned { get; init; }
}

public sealed class RedemptionOutcome
{
    public required Redemption Redemption { get; init; }
    public required CustomerSummary Summary { get; init; }
    public decimal DiscountValue { get; init; }
}

/// <summary>
/// Toate operațiunile programului de fidelizare. Fiecare metodă publică deschide
/// o singură tranzacție și o confirmă la final, deci o operațiune reușește complet
/// sau nu lasă nicio urmă.
/// </summary>
public sealed class LoyaltyService
{
    private readonly IUnitOfWorkFactory _factory;
    private readonly IClock _clock;

    public LoyaltyService(IUnitOfWorkFactory factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    // ---------------------------------------------------------------- Reguli

    public LoyaltyRules GetRules()
    {
        using var uow = _factory.Create();
        return LoyaltyRules.Load(uow.Settings);
    }

    public IReadOnlyList<Tier> GetTiers()
    {
        using var uow = _factory.Create();
        return uow.Tiers.GetAll();
    }

    public OperationResult SaveRules(LoyaltyRules rules, AppUser actor)
    {
        var errors = rules.Validate();
        if (errors.Count > 0)
            return OperationResult.Fail(string.Join(Environment.NewLine, errors));

        using var uow = _factory.Create();
        var before = LoyaltyRules.Load(uow.Settings);
        rules.Save(uow.Settings);

        WriteAudit(uow, actor, "RulesChanged", "Settings", null, DescribeRuleChanges(before, rules));
        uow.Commit();
        return OperationResult.Ok();
    }

    // --------------------------------------------------------------- Clienți

    public IReadOnlyList<Customer> SearchCustomers(CustomerQuery query)
    {
        using var uow = _factory.Create();
        return uow.Customers.Search(query);
    }

    /// <summary>
    /// Caută clientul după codul de card sau după telefon. Este drumul rapid de la
    /// casă: se scanează cardul sau se tastează telefonul, fără a deschide o listă.
    /// </summary>
    public Customer? FindByCardOrPhone(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        using var uow = _factory.Create();

        var byCard = uow.Customers.GetByCardCode(text);
        if (byCard is not null)
            return byCard;

        var phone = NormalizePhone(text);
        return phone.Length >= 6 ? uow.Customers.GetByPhone(phone) : null;
    }

    public CustomerSummary? GetSummary(int customerId)
    {
        using var uow = _factory.Create();
        return BuildSummary(uow, customerId);
    }

    /// <param name="grantWelcomeBonus">
    /// Fals la importul clienților existenți: ei și-au făcut deja cumpărăturile,
    /// iar un bonus de bun venit acordat retroactiv ar denatura soldurile.
    /// </param>
    public OperationResult<Customer> EnrollCustomer(
        Customer customer, AppUser actor, bool grantWelcomeBonus = true)
    {
        var validation = ValidateCustomer(customer);
        if (validation is not null)
            return OperationResult<Customer>.Fail(validation);

        using var uow = _factory.Create();
        var rules = LoyaltyRules.Load(uow.Settings);
        var now = _clock.Now;

        customer.CardCode = string.IsNullOrWhiteSpace(customer.CardCode)
            ? NextCardCode(uow, rules)
            : customer.CardCode.Trim();

        customer.Phone = NormalizePhoneOrNull(customer.Phone);
        customer.Email = string.IsNullOrWhiteSpace(customer.Email) ? null : customer.Email.Trim();
        customer.EnrolledAt = now;
        customer.IsActive = true;

        if (customer.MarketingConsent && customer.ConsentAt is null)
            customer.ConsentAt = now;

        if (uow.Customers.CardCodeExists(customer.CardCode))
            return OperationResult<Customer>.Fail($"Codul de card {customer.CardCode} este deja folosit.");

        if (customer.Phone is not null && uow.Customers.PhoneExists(customer.Phone))
            return OperationResult<Customer>.Fail("Există deja un client înscris cu acest număr de telefon.");

        var fieldError = ValidateCustomValues(uow, customer);
        if (fieldError is not null)
            return OperationResult<Customer>.Fail(fieldError);

        customer.Id = uow.Customers.Insert(customer);
        uow.CustomFields.SetValues(customer.Id, customer.CustomValues);

        if (grantWelcomeBonus && rules.WelcomeBonusPoints > 0)
        {
            GrantPoints(
                uow,
                customer.Id,
                rules.WelcomeBonusPoints,
                PointLotSource.Welcome,
                PointTransactionType.Welcome,
                now,
                rules.GetExpiryDate(now),
                actor,
                "Bonus la înscrierea în program");
        }

        WriteAudit(uow, actor, "CustomerEnrolled", "Customer", customer.Id,
            $"{customer.FullName}, card {customer.CardCode}");

        uow.Commit();
        return OperationResult<Customer>.Ok(customer);
    }

    public OperationResult UpdateCustomer(Customer customer, AppUser actor)
    {
        var validation = ValidateCustomer(customer);
        if (validation is not null)
            return OperationResult.Fail(validation);

        using var uow = _factory.Create();

        var existing = uow.Customers.GetById(customer.Id);
        if (existing is null)
            return OperationResult.Fail("Clientul nu mai există în baza de date.");

        customer.CardCode = customer.CardCode.Trim();
        customer.Phone = NormalizePhoneOrNull(customer.Phone);
        customer.Email = string.IsNullOrWhiteSpace(customer.Email) ? null : customer.Email.Trim();

        if (uow.Customers.CardCodeExists(customer.CardCode, customer.Id))
            return OperationResult.Fail($"Codul de card {customer.CardCode} este folosit de alt client.");

        if (customer.Phone is not null && uow.Customers.PhoneExists(customer.Phone, customer.Id))
            return OperationResult.Fail("Numărul de telefon este folosit de alt client.");

        if (customer.MarketingConsent && !existing.MarketingConsent)
            customer.ConsentAt = _clock.Now;
        else if (!customer.MarketingConsent)
            customer.ConsentAt = null;

        // Câmpurile de sistem nu se lasă pe seama interfeței.
        customer.EnrolledAt = existing.EnrolledAt;
        customer.LastBirthdayBonusYear = existing.LastBirthdayBonusYear;

        var fieldError = ValidateCustomValues(uow, customer);
        if (fieldError is not null)
            return OperationResult.Fail(fieldError);

        uow.Customers.Update(customer);
        uow.CustomFields.SetValues(customer.Id, customer.CustomValues);

        WriteAudit(uow, actor, "CustomerUpdated", "Customer", customer.Id,
            DescribeCustomerChanges(existing, customer));

        uow.Commit();
        return OperationResult.Ok();
    }

    // -------------------------------------------------------------- Vânzări

    /// <summary>
    /// Înregistrează o vânzare și acordă punctele corespunzătoare. Punctele se
    /// calculează cu multiplicatorul nivelului pe care clientul îl avea în
    /// momentul vânzării, nu cu cel la care ajunge după ea.
    /// </summary>
    public OperationResult<PurchaseOutcome> RecordPurchase(PurchaseRequest request, AppUser actor)
    {
        if (request.GrossAmount <= 0)
            return OperationResult<PurchaseOutcome>.Fail("Valoarea bonului trebuie să fie mai mare decât zero.");

        if (request.DiscountAmount < 0)
            return OperationResult<PurchaseOutcome>.Fail("Reducerea nu poate fi negativă.");

        if (request.DiscountAmount > request.GrossAmount)
            return OperationResult<PurchaseOutcome>.Fail("Reducerea nu poate depăși valoarea bonului.");

        using var uow = _factory.Create();

        var customer = uow.Customers.GetById(request.CustomerId);
        if (customer is null)
            return OperationResult<PurchaseOutcome>.Fail("Clientul nu a fost găsit.");

        if (!customer.IsActive)
            return OperationResult<PurchaseOutcome>.Fail("Clientul este dezactivat și nu poate acumula puncte.");

        var receipt = string.IsNullOrWhiteSpace(request.ReceiptNumber) ? null : request.ReceiptNumber.Trim();
        if (receipt is not null && uow.Purchases.ReceiptNumberExists(receipt))
            return OperationResult<PurchaseOutcome>.Fail($"Bonul {receipt} a fost deja înregistrat.");

        var occurredAt = request.OccurredAt ?? _clock.Now;
        if (occurredAt > _clock.Now.AddMinutes(5))
            return OperationResult<PurchaseOutcome>.Fail("Data vânzării nu poate fi în viitor.");

        var rules = LoyaltyRules.Load(uow.Settings);
        var tiers = uow.Tiers.GetAll();

        var tierBefore = ResolveTier(uow, customer.Id, rules, tiers, occurredAt);

        var net = Math.Round(request.GrossAmount - request.DiscountAmount, 2, MidpointRounding.AwayFromZero);

        // Dacă mai multe campanii se suprapun, se aplică cea mai generoasă. Produsul
        // lor ar duce la multiplicatori pe care nimeni nu i-a intenționat.
        var campaign = uow.Campaigns.GetRunningOn(occurredAt)
            .Where(c => c.AppliesTo(occurredAt, net))
            .OrderByDescending(c => c.Multiplier)
            .FirstOrDefault();

        var campaignMultiplier = campaign?.Multiplier ?? 1m;
        var points = PointsCalculator.PointsForPurchase(
            net, tierBefore.EarnMultiplier * campaignMultiplier, rules);

        var purchase = new Purchase
        {
            CustomerId = customer.Id,
            ReceiptNumber = receipt,
            GrossAmount = Math.Round(request.GrossAmount, 2, MidpointRounding.AwayFromZero),
            DiscountAmount = Math.Round(request.DiscountAmount, 2, MidpointRounding.AwayFromZero),
            NetAmount = net,
            PointsEarned = points,
            TierAtPurchase = tierBefore.Name,
            MultiplierApplied = tierBefore.EarnMultiplier,
            CampaignName = campaign?.Name,
            CampaignMultiplier = campaignMultiplier,
            OccurredAt = occurredAt,
            RecordedByUserId = actor.Id,
            RecordedByUserName = actor.Username,
        };

        purchase.Id = uow.Purchases.Insert(purchase);

        if (points > 0)
        {
            GrantPoints(
                uow,
                customer.Id,
                points,
                PointLotSource.Purchase,
                PointTransactionType.Earned,
                occurredAt,
                rules.GetExpiryDate(occurredAt),
                actor,
                $"Cumpărare de {net:0.##} lei" + (receipt is null ? "" : $", bon {receipt}"),
                purchaseId: purchase.Id);
        }

        var summary = BuildSummary(uow, customer.Id)!;
        var promoted = summary.Tier.Threshold > tierBefore.Threshold ? summary.Tier : null;

        WriteAudit(uow, actor, "PurchaseRecorded", "Purchase", purchase.Id,
            $"Client {customer.FullName}, net {net:0.##} lei, {points} puncte");

        uow.Commit();

        return OperationResult<PurchaseOutcome>.Ok(new PurchaseOutcome
        {
            Purchase = purchase,
            Summary = summary,
            PointsEarned = points,
            PromotedToTier = promoted,
        });
    }

    /// <summary>
    /// Anulează o vânzare și retrage punctele acordate. Dacă între timp clientul a
    /// cheltuit o parte din ele, se retrag din loturile rămase, în ordinea expirării.
    /// Dacă nici acestea nu ajung, soldul coboară la zero și diferența rămasă se
    /// consemnează — nu se lasă un sold negativ, care ar bloca clientul la casă.
    /// </summary>
    public OperationResult VoidPurchase(int purchaseId, string reason, AppUser actor)
    {
        if (!actor.CanManagePoints)
            return OperationResult.Fail("Anularea unei vânzări necesită drepturi de responsabil.");

        if (string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("Motivul anulării este obligatoriu.");

        using var uow = _factory.Create();

        var purchase = uow.Purchases.GetById(purchaseId);
        if (purchase is null)
            return OperationResult.Fail("Vânzarea nu a fost găsită.");

        if (purchase.IsVoided)
            return OperationResult.Fail("Vânzarea este deja anulată.");

        var now = _clock.Now;

        // Punctele retrase deja prin retururi parțiale nu se mai retrag o dată.
        var toReverse = Math.Max(0, purchase.PointsEarned - purchase.PointsReturned);

        var shortfall = RetractPurchasePoints(
            uow, purchase, toReverse, PointTransactionType.ReversalOfEarning,
            $"Anulare vânzare din {purchase.OccurredAt:dd.MM.yyyy}: {reason.Trim()}", actor, now);

        purchase.IsVoided = true;
        purchase.VoidedAt = now;
        purchase.VoidReason = reason.Trim();
        uow.Purchases.Update(purchase);

        WriteAudit(uow, actor, "PurchaseVoided", "Purchase", purchase.Id,
            $"Net {purchase.NetAmount:0.##} lei, {toReverse} puncte retrase" +
            (shortfall > 0 ? $" (lipsă {shortfall})" : "") + $". Motiv: {reason.Trim()}");

        uow.Commit();

        return shortfall > 0
            ? OperationResult.Fail(
                $"Vânzarea a fost anulată, dar {shortfall} puncte fuseseră deja consumate și nu au putut fi retrase. " +
                "Soldul clientului a fost adus la zero.")
            : OperationResult.Ok();
    }

    // ---------------------------------------------------------------- Retururi

    public Purchase? GetPurchase(int purchaseId)
    {
        using var uow = _factory.Create();
        return uow.Purchases.GetById(purchaseId);
    }

    /// <summary>Bonul după numărul lui, pentru returul pornit de la casă cu bonul în mână.</summary>
    public Purchase? FindPurchaseByReceipt(string receiptNumber)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber))
            return null;

        using var uow = _factory.Create();
        return uow.Purchases.GetByReceiptNumber(receiptNumber.Trim());
    }

    public IReadOnlyList<PurchaseReturn> GetReturns(int purchaseId)
    {
        using var uow = _factory.Create();
        return uow.Purchases.GetReturnsForPurchase(purchaseId);
    }

    /// <summary>
    /// Returul unei părți dintr-un bon, sau al întregului rest. Se retrag punctele
    /// proporționale cu suma returnată, iar suma iese și din rulajul pe care se
    /// calculează nivelul. Când tot bonul a fost returnat, bonul se închide ca
    /// anulat, ca să nu mai conteze nici la numărul de cumpărături.
    ///
    /// Ca la anulare, dacă punctele au fost deja cheltuite, soldul coboară la zero
    /// și nu devine negativ; diferența rămâne consemnată pe retur.
    ///
    /// Spre deosebire de anulare, returul îl poate face și casierul: clientul vine
    /// cu produsele când e deschis magazinul, iar responsabilul poate să nu fie
    /// acolo. Operatorul rămâne trecut pe retur și în jurnalul de operațiuni.
    /// </summary>
    public OperationResult<ReturnOutcome> ReturnPurchase(ReturnRequest request, AppUser actor)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return OperationResult<ReturnOutcome>.Fail("Motivul returului este obligatoriu.");

        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            return OperationResult<ReturnOutcome>.Fail("Suma returnată trebuie să fie mai mare decât zero.");

        using var uow = _factory.Create();

        var purchase = uow.Purchases.GetById(request.PurchaseId);
        if (purchase is null)
            return OperationResult<ReturnOutcome>.Fail("Vânzarea nu a fost găsită.");

        if (purchase.IsVoided)
            return OperationResult<ReturnOutcome>.Fail(
                purchase.HasReturns && purchase.RemainingAmount == 0
                    ? "Bonul a fost deja returnat integral."
                    : "Vânzarea este anulată, nu se mai poate face retur pe ea.");

        if (amount > purchase.RemainingAmount)
            return OperationResult<ReturnOutcome>.Fail(
                $"Suma returnată depășește ce a rămas din bon ({purchase.RemainingAmount:N2} lei).");

        var now = _clock.Now;
        var reason = request.Reason.Trim();
        var points = PointsCalculator.PointsToReverseForReturn(purchase, amount);

        var shortfall = RetractPurchasePoints(
            uow, purchase, points, PointTransactionType.Return,
            $"Retur {amount:0.##} lei din bonul din {purchase.OccurredAt:dd.MM.yyyy}: {reason}", actor, now);

        var purchaseReturn = new PurchaseReturn
        {
            PurchaseId = purchase.Id,
            CustomerId = purchase.CustomerId,
            Amount = amount,
            PointsReversed = points,
            PointsShortfall = shortfall,
            Reason = reason,
            OccurredAt = now,
            UserId = actor.Id,
            UserName = actor.Username,
        };

        purchaseReturn.Id = uow.Purchases.InsertReturn(purchaseReturn);

        purchase.ReturnedAmount += amount;
        purchase.PointsReturned += points;

        var fullyReturned = purchase.RemainingAmount == 0;
        if (fullyReturned)
        {
            purchase.IsVoided = true;
            purchase.VoidedAt = now;
            purchase.VoidReason = $"Retur integral: {reason}";
        }

        uow.Purchases.Update(purchase);

        WriteAudit(uow, actor, "PurchaseReturned", "Purchase", purchase.Id,
            $"Retur {amount:0.##} lei din {purchase.NetAmount:0.##} lei, {points} puncte retrase" +
            (shortfall > 0 ? $" (lipsă {shortfall})" : "") +
            (fullyReturned ? ", bon returnat integral" : "") + $". Motiv: {reason}");

        var summary = BuildSummary(uow, purchase.CustomerId)!;
        uow.Commit();

        return OperationResult<ReturnOutcome>.Ok(new ReturnOutcome
        {
            Return = purchaseReturn,
            Purchase = purchase,
            Summary = summary,
            FullyReturned = fullyReturned,
        });
    }

    // ------------------------------------------------------------ Recompense

    /// <summary>Recompensele pe care clientul le poate obține acum, cu motivul blocării unde e cazul.</summary>
    public IReadOnlyList<(Reward Reward, string? Blocker)> GetRewardsFor(int customerId, decimal basketAmount)
    {
        using var uow = _factory.Create();

        var summary = BuildSummary(uow, customerId);
        if (summary is null)
            return [];

        var rules = LoyaltyRules.Load(uow.Settings);
        var tiers = uow.Tiers.GetAll();
        var now = _clock.Now;

        return uow.Rewards.GetAll()
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.PointsCost)
            .Select(r => (r, PointsCalculator.GetRedemptionBlocker(
                r, summary.PointsBalance, basketAmount, summary.Tier, tiers, rules, now)))
            .ToList();
    }

    public OperationResult<RedemptionOutcome> RedeemReward(RedeemRequest request, AppUser actor)
    {
        using var uow = _factory.Create();

        var customer = uow.Customers.GetById(request.CustomerId);
        if (customer is null)
            return OperationResult<RedemptionOutcome>.Fail("Clientul nu a fost găsit.");

        if (!customer.IsActive)
            return OperationResult<RedemptionOutcome>.Fail("Clientul este dezactivat.");

        var reward = uow.Rewards.GetById(request.RewardId);
        if (reward is null)
            return OperationResult<RedemptionOutcome>.Fail("Recompensa nu a fost găsită.");

        var rules = LoyaltyRules.Load(uow.Settings);
        var tiers = uow.Tiers.GetAll();
        var now = _clock.Now;

        // Punctele expirate se scot din calcul înainte de a verifica soldul,
        // altfel s-ar putea revendica o recompensă cu puncte care nu mai sunt valabile.
        ExpireDueLots(uow, now, customer.Id, actor);

        var summary = BuildSummary(uow, customer.Id)!;

        var blocker = PointsCalculator.GetRedemptionBlocker(
            reward, summary.PointsBalance, request.BasketAmount, summary.Tier, tiers, rules, now);

        if (blocker is not null)
            return OperationResult<RedemptionOutcome>.Fail(blocker);

        var discount = PointsCalculator.CalculateRewardDiscount(reward, request.BasketAmount);

        if (reward.Kind != RewardKind.FreeProduct && discount <= 0)
            return OperationResult<RedemptionOutcome>.Fail(
                "Reducerea calculată este zero. Verifică valoarea coșului.");

        var consumed = ConsumeLots(uow, customer.Id, reward.PointsCost);
        var consumedTotal = consumed.Sum(c => c.Points);

        if (consumedTotal < reward.PointsCost)
            return OperationResult<RedemptionOutcome>.Fail(
                "Soldul de puncte s-a modificat între timp. Reîncearcă operațiunea.");

        var receipt = string.IsNullOrWhiteSpace(request.ReceiptNumber) ? null : request.ReceiptNumber.Trim();

        var transactionId = uow.Points.InsertTransaction(new PointTransaction
        {
            CustomerId = customer.Id,
            Type = PointTransactionType.Redeemed,
            Points = -reward.PointsCost,
            BalanceAfter = uow.Points.GetBalance(customer.Id),
            OccurredAt = now,
            UserId = actor.Id,
            UserName = actor.Username,
            Note = $"{reward.Name} ({reward.ValueLabel})",
        });

        foreach (var (lot, points) in consumed)
        {
            uow.Points.InsertConsumption(new PointLotConsumption
            {
                PointLotId = lot.Id,
                PointTransactionId = transactionId,
                Points = points,
                OccurredAt = now,
            });
        }

        var redemption = new Redemption
        {
            CustomerId = customer.Id,
            RewardId = reward.Id,
            RewardName = reward.Name,
            Kind = reward.Kind,
            PointsSpent = reward.PointsCost,
            DiscountValue = discount,
            BasketAmount = request.BasketAmount > 0 ? request.BasketAmount : null,
            ReceiptNumber = receipt,
            PointTransactionId = transactionId,
            OccurredAt = now,
            UserId = actor.Id,
            UserName = actor.Username,
        };

        redemption.Id = uow.Redemptions.Insert(redemption);

        WriteAudit(uow, actor, "RewardRedeemed", "Redemption", redemption.Id,
            $"Client {customer.FullName}, {reward.Name}, {reward.PointsCost} puncte, {discount:0.##} lei");

        var finalSummary = BuildSummary(uow, customer.Id)!;
        uow.Commit();

        return OperationResult<RedemptionOutcome>.Ok(new RedemptionOutcome
        {
            Redemption = redemption,
            Summary = finalSummary,
            DiscountValue = discount,
        });
    }

    /// <summary>
    /// Anulează o recompensă și restituie punctele în loturile din care fuseseră
    /// luate, cu datele lor originale de expirare. Punctele ale căror loturi au
    /// expirat între timp nu se mai restituie — au fost oricum pierdute.
    /// </summary>
    public OperationResult VoidRedemption(int redemptionId, string reason, AppUser actor)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return OperationResult.Fail("Motivul anulării este obligatoriu.");

        using var uow = _factory.Create();

        var redemption = uow.Redemptions.GetById(redemptionId);
        if (redemption is null)
            return OperationResult.Fail("Recompensa acordată nu a fost găsită.");

        if (redemption.IsVoided)
            return OperationResult.Fail("Recompensa este deja anulată.");

        var now = _clock.Now;

        // Responsabilul poate anula oricând. Casierul își poate anula doar propria
        // acordare, și doar în intervalul stabilit: atât timp cât clientul este
        // încă la casă, este o corectură pe loc, nu o modificare de istoric.
        if (!actor.CanManagePoints)
        {
            var rules = LoyaltyRules.Load(uow.Settings);
            var minutes = (now - redemption.OccurredAt).TotalMinutes;

            if (redemption.UserId != actor.Id)
                return OperationResult.Fail("Poți anula doar recompensele acordate de tine.");

            if (rules.SelfVoidMinutes <= 0)
                return OperationResult.Fail("Anularea unei recompense necesită drepturi de responsabil.");

            if (minutes > rules.SelfVoidMinutes)
            {
                return OperationResult.Fail(
                    $"Au trecut mai mult de {rules.SelfVoidMinutes} minute de la acordare. " +
                    "Anularea trebuie făcută de un responsabil.");
            }
        }
        var restored = 0;
        var lost = 0;

        foreach (var consumption in uow.Points.GetConsumptionsForTransaction(redemption.PointTransactionId))
        {
            var lot = uow.Points.GetLotById(consumption.PointLotId);
            if (lot is null)
            {
                lost += consumption.Points;
                continue;
            }

            if (lot.IsExpired || (lot.ExpiresAt is { } exp && exp <= now))
            {
                lost += consumption.Points;
                continue;
            }

            lot.PointsRemaining += consumption.Points;
            uow.Points.UpdateLot(lot);
            restored += consumption.Points;
        }

        if (restored > 0)
        {
            var note = $"Anulare recompensă „{redemption.RewardName}”: {reason.Trim()}";
            if (lost > 0)
                note += $" ({lost} puncte nu s-au restituit, valabilitatea lor expirase)";

            uow.Points.InsertTransaction(new PointTransaction
            {
                CustomerId = redemption.CustomerId,
                Type = PointTransactionType.ReversalOfRedemption,
                Points = restored,
                BalanceAfter = uow.Points.GetBalance(redemption.CustomerId),
                RedemptionId = redemption.Id,
                OccurredAt = now,
                UserId = actor.Id,
                UserName = actor.Username,
                Note = note,
            });
        }

        redemption.IsVoided = true;
        redemption.VoidedAt = now;
        redemption.VoidReason = reason.Trim();
        uow.Redemptions.Update(redemption);

        WriteAudit(uow, actor, "RedemptionVoided", "Redemption", redemption.Id,
            $"{redemption.RewardName}, {restored} puncte restituite" +
            (lost > 0 ? $", {lost} expirate" : "") + $". Motiv: {reason.Trim()}");

        uow.Commit();

        return lost > 0
            ? OperationResult.Fail(
                $"Recompensa a fost anulată și s-au restituit {restored} puncte. " +
                $"Restul de {lost} puncte aveau valabilitatea expirată și nu au putut fi restituite.")
            : OperationResult.Ok();
    }

    // ----------------------------------------------------- Corecții și bonusuri

    /// <summary>Corecție manuală de puncte, în plus sau în minus, cu motiv obligatoriu.</summary>
    public OperationResult AdjustPoints(int customerId, int points, string note, AppUser actor)
    {
        if (!actor.CanManagePoints)
            return OperationResult.Fail("Corecțiile de puncte necesită drepturi de responsabil.");

        if (points == 0)
            return OperationResult.Fail("Numărul de puncte nu poate fi zero.");

        if (string.IsNullOrWhiteSpace(note))
            return OperationResult.Fail("Motivul corecției este obligatoriu.");

        using var uow = _factory.Create();

        var customer = uow.Customers.GetById(customerId);
        if (customer is null)
            return OperationResult.Fail("Clientul nu a fost găsit.");

        var rules = LoyaltyRules.Load(uow.Settings);
        var now = _clock.Now;

        if (points > 0)
        {
            GrantPoints(uow, customerId, points, PointLotSource.ManualAdjustment,
                PointTransactionType.AdjustmentPlus, now, rules.GetExpiryDate(now), actor, note.Trim());
        }
        else
        {
            var wanted = -points;
            var balance = uow.Points.GetBalance(customerId);
            if (balance < wanted)
                return OperationResult.Fail($"Clientul are doar {balance} puncte, nu se pot scădea {wanted}.");

            var consumed = ConsumeLots(uow, customerId, wanted);

            var transactionId = uow.Points.InsertTransaction(new PointTransaction
            {
                CustomerId = customerId,
                Type = PointTransactionType.AdjustmentMinus,
                Points = -wanted,
                BalanceAfter = uow.Points.GetBalance(customerId),
                OccurredAt = now,
                UserId = actor.Id,
                UserName = actor.Username,
                Note = note.Trim(),
            });

            foreach (var (lot, taken) in consumed)
            {
                uow.Points.InsertConsumption(new PointLotConsumption
                {
                    PointLotId = lot.Id,
                    PointTransactionId = transactionId,
                    Points = taken,
                    OccurredAt = now,
                });
            }
        }

        WriteAudit(uow, actor, "PointsAdjusted", "Customer", customerId,
            $"{(points > 0 ? "+" : "")}{points} puncte pentru {customer.FullName}. Motiv: {note.Trim()}");

        uow.Commit();
        return OperationResult.Ok();
    }

    /// <summary>Acordă bonusul de zi de naștere, dacă este cazul și nu a fost deja acordat anul acesta.</summary>
    public OperationResult<int> GrantBirthdayBonus(int customerId, AppUser actor)
    {
        using var uow = _factory.Create();

        var customer = uow.Customers.GetById(customerId);
        if (customer is null)
            return OperationResult<int>.Fail("Clientul nu a fost găsit.");

        var rules = LoyaltyRules.Load(uow.Settings);
        var now = _clock.Now;

        if (rules.BirthdayBonusPoints <= 0)
            return OperationResult<int>.Fail("Bonusul de zi de naștere este dezactivat în setări.");

        if (customer.BirthDate is null)
            return OperationResult<int>.Fail("Clientul nu are data nașterii completată.");

        if (customer.LastBirthdayBonusYear == now.Year)
            return OperationResult<int>.Fail("Bonusul a fost deja acordat anul acesta.");

        if (!PointsCalculator.IsWithinBirthdayWindow(customer.BirthDate.Value, now, rules.BirthdayWindowDays))
            return OperationResult<int>.Fail(
                $"Bonusul se poate acorda doar în intervalul de {rules.BirthdayWindowDays} zile din jurul aniversării.");

        GrantPoints(uow, customer.Id, rules.BirthdayBonusPoints, PointLotSource.Birthday,
            PointTransactionType.Birthday, now, rules.GetExpiryDate(now), actor, "Bonus de zi de naștere");

        customer.LastBirthdayBonusYear = now.Year;
        uow.Customers.Update(customer);

        WriteAudit(uow, actor, "BirthdayBonusGranted", "Customer", customer.Id,
            $"{rules.BirthdayBonusPoints} puncte pentru {customer.FullName}");

        uow.Commit();
        return OperationResult<int>.Ok(rules.BirthdayBonusPoints);
    }

    /// <summary>
    /// Trece prin toate loturile ajunse la termen și le scoate din sold.
    /// Se rulează la pornirea aplicației; este idempotentă, deci nu strică
    /// nimic dacă se rulează de mai multe ori pe zi.
    /// </summary>
    public int RunExpiry(AppUser? actor = null)
    {
        using var uow = _factory.Create();
        var expired = ExpireDueLots(uow, _clock.Now, customerId: null, actor);

        if (expired > 0)
            uow.Settings.Set(LoyaltyRules.KeyLastExpiryRun, _clock.Now.ToString("O"));

        uow.Commit();
        return expired;
    }

    /// <summary>Clienții care au ziua de naștere în perioada apropiată și încă nu au primit bonusul.</summary>
    public IReadOnlyList<Customer> GetUpcomingBirthdays(int daysAhead = 7)
    {
        using var uow = _factory.Create();
        var rules = LoyaltyRules.Load(uow.Settings);
        var today = _clock.Today;

        return uow.Customers
            .GetBirthdaysBetween(today.AddDays(-rules.BirthdayWindowDays), today.AddDays(daysAhead))
            .Where(c => c.IsActive && c.LastBirthdayBonusYear != today.Year)
            .ToList();
    }

    /// <summary>Definițiile câmpurilor adăugate de magazin pe fișa clientului.</summary>
    public IReadOnlyList<CustomFieldDefinition> GetCustomFields(bool includeInactive = false)
    {
        using var uow = _factory.Create();
        return uow.CustomFields.GetDefinitions(includeInactive);
    }

    /// <summary>Clientul împreună cu valorile câmpurilor proprii, pentru formularul de editare.</summary>
    public Customer? GetCustomer(int customerId)
    {
        using var uow = _factory.Create();

        var customer = uow.Customers.GetById(customerId);
        if (customer is not null)
            LoadCustomValues(uow, customer);

        return customer;
    }

    /// <summary>
    /// Campania care s-ar aplica acum unui bon de valoarea dată, sau null dacă nu
    /// există niciuna. Folosită la casă pentru a arăta punctele înainte de încasare.
    /// </summary>
    public Campaign? GetActiveCampaign(decimal netAmount)
    {
        using var uow = _factory.Create();

        return uow.Campaigns.GetRunningOn(_clock.Now)
            .Where(c => c.AppliesTo(_clock.Now, netAmount))
            .OrderByDescending(c => c.Multiplier)
            .FirstOrDefault();
    }

    public IReadOnlyList<Campaign> GetCampaigns()
    {
        using var uow = _factory.Create();
        return uow.Campaigns.GetAll();
    }

    public string GenerateCardCode()
    {
        using var uow = _factory.Create();
        return NextCardCode(uow, LoyaltyRules.Load(uow.Settings));
    }

    // ---------------------------------------------------------------- Interne

    /// <summary>
    /// Creează un lot nou și linia de registru corespunzătoare. Toate căile prin
    /// care se acordă puncte trec pe aici, ca istoricul să arate la fel indiferent
    /// dacă punctele vin dintr-o vânzare, dintr-un bonus sau dintr-o corecție.
    /// </summary>
    private static void GrantPoints(
        IUnitOfWork uow,
        int customerId,
        int points,
        PointLotSource source,
        PointTransactionType type,
        DateTime at,
        DateTime? expiresAt,
        AppUser actor,
        string note,
        int? purchaseId = null)
    {
        if (points <= 0)
            return;

        var lot = new PointLot
        {
            CustomerId = customerId,
            Source = source,
            PurchaseId = purchaseId,
            PointsOriginal = points,
            PointsRemaining = points,
            EarnedAt = at,
            ExpiresAt = expiresAt,
        };

        lot.Id = uow.Points.InsertLot(lot);

        uow.Points.InsertTransaction(new PointTransaction
        {
            CustomerId = customerId,
            Type = type,
            Points = points,
            BalanceAfter = uow.Points.GetBalance(customerId),
            PointLotId = lot.Id,
            PurchaseId = purchaseId,
            OccurredAt = at,
            UserId = actor.Id,
            UserName = actor.Username,
            Note = note,
        });
    }

    /// <summary>
    /// Retrage punctele unei vânzări, la anulare sau la retur. Întâi din loturile
    /// generate chiar de vânzare, apoi din celelalte loturi deschise, în ordinea
    /// expirării. Ce nu se mai găsește în sold nu se retrage: soldul nu devine
    /// negativ. Întoarce câte puncte au lipsit.
    /// </summary>
    private static int RetractPurchasePoints(
        IUnitOfWork uow,
        Purchase purchase,
        int points,
        PointTransactionType type,
        string note,
        AppUser actor,
        DateTime at)
    {
        if (points <= 0)
            return 0;

        var ownLots = uow.Points.GetLotsForPurchase(purchase.Id);
        var ownIds = ownLots.Select(l => l.Id).ToHashSet();
        var candidates = ownLots.Concat(uow.Points.GetOpenLots(purchase.CustomerId).Where(l => !ownIds.Contains(l.Id)));

        var consumed = new List<(PointLot Lot, int Points)>();
        var remaining = points;

        foreach (var lot in candidates)
        {
            if (remaining <= 0) break;
            var take = Math.Min(lot.PointsRemaining, remaining);
            if (take <= 0) continue;

            lot.PointsRemaining -= take;
            uow.Points.UpdateLot(lot);
            consumed.Add((lot, take));
            remaining -= take;
        }

        var reversed = points - remaining;
        if (reversed <= 0)
            return remaining;

        if (remaining > 0)
            note += $" (nu s-au putut retrage {remaining} puncte, deja consumate)";

        var transactionId = uow.Points.InsertTransaction(new PointTransaction
        {
            CustomerId = purchase.CustomerId,
            Type = type,
            Points = -reversed,
            BalanceAfter = uow.Points.GetBalance(purchase.CustomerId),
            PurchaseId = purchase.Id,
            OccurredAt = at,
            UserId = actor.Id,
            UserName = actor.Username,
            Note = note,
        });

        foreach (var (lot, taken) in consumed)
        {
            uow.Points.InsertConsumption(new PointLotConsumption
            {
                PointLotId = lot.Id,
                PointTransactionId = transactionId,
                Points = taken,
                OccurredAt = at,
            });
        }

        return remaining;
    }

    /// <summary>
    /// Ia punctele cerute din loturile deschise, în ordinea expirării: primele
    /// consumate sunt cele care expiră cel mai devreme, ca ele să nu se piardă.
    /// Întoarce ce s-a luat din fiecare lot, pentru evidența consumului.
    /// </summary>
    private static List<(PointLot Lot, int Points)> ConsumeLots(IUnitOfWork uow, int customerId, int points)
    {
        var result = new List<(PointLot, int)>();
        var remaining = points;

        foreach (var lot in uow.Points.GetOpenLots(customerId))
        {
            if (remaining <= 0)
                break;

            var take = Math.Min(lot.PointsRemaining, remaining);
            if (take <= 0)
                continue;

            lot.PointsRemaining -= take;
            uow.Points.UpdateLot(lot);

            result.Add((lot, take));
            remaining -= take;
        }

        return result;
    }

    /// <summary>Scoate din sold loturile ajunse la termen. Întoarce câte puncte s-au pierdut.</summary>
    private static int ExpireDueLots(IUnitOfWork uow, DateTime asOf, int? customerId, AppUser? actor)
    {
        var total = 0;

        foreach (var lot in uow.Points.GetLotsToExpire(asOf, customerId))
        {
            var lost = lot.PointsRemaining;
            if (lost <= 0)
            {
                lot.IsExpired = true;
                uow.Points.UpdateLot(lot);
                continue;
            }

            lot.PointsRemaining = 0;
            lot.IsExpired = true;
            uow.Points.UpdateLot(lot);

            uow.Points.InsertTransaction(new PointTransaction
            {
                CustomerId = lot.CustomerId,
                Type = PointTransactionType.Expired,
                Points = -lost,
                BalanceAfter = uow.Points.GetBalance(lot.CustomerId),
                PointLotId = lot.Id,
                OccurredAt = asOf,
                UserId = actor?.Id,
                UserName = actor?.Username,
                Note = $"Puncte acordate pe {lot.EarnedAt:dd.MM.yyyy}, expirate pe {lot.ExpiresAt:dd.MM.yyyy}",
            });

            total += lost;
        }

        return total;
    }

    /// <summary>
    /// Valoarea pe care se compară pragurile de nivel, în unitatea aleasă de magazin:
    /// lei cheltuiți, puncte acumulate sau număr de cumpărături.
    /// </summary>
    private static decimal GetQualifyingValue(
        IUnitOfWork uow, int customerId, LoyaltyRules rules, DateTime asOf)
    {
        var since = rules.GetQualifyingPeriodStart(asOf);

        return rules.TierBasis switch
        {
            TierBasis.PointsEarned => uow.Points.GetPointsEarnedSince(customerId, since),
            TierBasis.PurchaseCount => uow.Purchases.GetPurchaseCountSince(customerId, since),
            _ => uow.Purchases.GetNetSpendSince(customerId, since),
        };
    }

    /// <summary>Nivelul clientului, după baza de calcul configurată.</summary>
    private static Tier ResolveTier(
        IUnitOfWork uow, int customerId, LoyaltyRules rules, IReadOnlyList<Tier> tiers, DateTime asOf) =>
        PointsCalculator.DetermineTier(GetQualifyingValue(uow, customerId, rules, asOf), tiers);

    private CustomerSummary? BuildSummary(IUnitOfWork uow, int customerId)
    {
        var customer = uow.Customers.GetById(customerId);
        if (customer is null)
            return null;

        LoadCustomValues(uow, customer);

        var rules = LoyaltyRules.Load(uow.Settings);
        var tiers = uow.Tiers.GetAll();
        var now = _clock.Now;

        var qualifying = GetQualifyingValue(uow, customerId, rules, now);
        var tier = PointsCalculator.DetermineTier(qualifying, tiers);
        var next = PointsCalculator.GetNextTier(tier, tiers);

        return new CustomerSummary
        {
            Customer = customer,
            PointsBalance = uow.Points.GetBalance(customerId),
            Tier = tier,
            NextTier = next,
            QualifyingValue = qualifying,
            ValueToNextTier = next is null ? 0m : Math.Max(0m, next.Threshold - qualifying),
            LifetimeSpend = uow.Purchases.GetLifetimeSpend(customerId),
            PurchaseCount = uow.Purchases.GetPurchaseCount(customerId),
            LastPurchaseAt = uow.Purchases.GetLastPurchaseDate(customerId),
            PointsExpiringSoon = rules.PointsValidityMonths > 0
                ? uow.Points.GetPointsExpiringBefore(customerId, now.Date.AddDays(rules.ExpiryWarningDays + 1))
                : 0,
            NextExpiryAt = uow.Points.GetNextExpiryDate(customerId),
        };
    }

    /// <summary>Umple valorile câmpurilor proprii pe obiectul clientului.</summary>
    private static void LoadCustomValues(IUnitOfWork uow, Customer customer)
    {
        customer.CustomValues.Clear();

        foreach (var (fieldId, value) in uow.CustomFields.GetValues(customer.Id))
            customer.CustomValues[fieldId] = value;
    }

    /// <summary>
    /// Verifică valorile câmpurilor definite de magazin. Câmpurile inactive sunt
    /// ignorate: o valoare veche rămasă pe un câmp scos din uz nu trebuie să
    /// împiedice salvarea clientului.
    /// </summary>
    private static string? ValidateCustomValues(IUnitOfWork uow, Customer customer)
    {
        foreach (var definition in uow.CustomFields.GetDefinitions())
        {
            customer.CustomValues.TryGetValue(definition.Id, out var value);

            var error = definition.Validate(value);
            if (error is not null)
                return error;
        }

        return null;
    }

    private static void WriteAudit(
        IUnitOfWork uow, AppUser? actor, string action, string? entityType, int? entityId, string? details)
    {
        uow.Audit.Insert(new AuditEntry
        {
            OccurredAt = DateTime.Now,
            UserId = actor?.Id,
            UserName = actor?.Username,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
        });
    }

    /// <summary>
    /// Următorul cod de card liber, în formatul stabilit de magazin: un prefix
    /// opțional și un număr cu un anumit număr de cifre. Codurile rămân consecutive,
    /// ca să poată fi tipărite ca serie de coduri de bare.
    /// </summary>
    private static string NextCardCode(IUnitOfWork uow, LoyaltyRules rules)
    {
        var digits = Math.Clamp(rules.CardCodeDigits, 3, 12);
        var prefix = rules.CardCodePrefix ?? string.Empty;

        // Prima valoare are toate cifrele ocupate: un cod de 6 cifre pornește de la
        // 100000, ca să nu existe coduri mai scurte decât restul.
        var floor = (long)Math.Pow(10, digits - 1);
        var next = Math.Max(uow.Customers.GetMaxNumericCardCode() + 1, floor);

        var format = new string('0', digits);
        var code = prefix + next.ToString(format);

        // În cazul improbabil în care codul generat există deja (import manual),
        // se caută mai departe până la unul liber.
        while (uow.Customers.CardCodeExists(code))
        {
            next++;
            code = prefix + next.ToString(format);
        }

        return code;
    }

    private static string? ValidateCustomer(Customer customer)
    {
        if (string.IsNullOrWhiteSpace(customer.LastName))
            return "Numele este obligatoriu.";

        if (string.IsNullOrWhiteSpace(customer.FirstName))
            return "Prenumele este obligatoriu.";

        var phone = NormalizePhoneOrNull(customer.Phone);
        if (phone is not null && (phone.Length < 6 || phone.Length > 15))
            return "Numărul de telefon nu pare valid.";

        if (!string.IsNullOrWhiteSpace(customer.Email) &&
            (!customer.Email.Contains('@') || customer.Email.Trim().Length < 5))
            return "Adresa de e-mail nu pare validă.";

        if (customer.BirthDate is { } birth)
        {
            if (birth > DateTime.Today)
                return "Data nașterii nu poate fi în viitor.";
            if (birth < DateTime.Today.AddYears(-120))
                return "Data nașterii nu pare validă.";
        }

        return null;
    }

    /// <summary>Păstrează doar cifrele, ca „0722 123 456” și „0722123456” să fie același număr.</summary>
    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var sb = new StringBuilder(phone.Length);
        foreach (var c in phone)
        {
            if (char.IsDigit(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static string? NormalizePhoneOrNull(string? phone)
    {
        var normalized = NormalizePhone(phone);
        return normalized.Length == 0 ? null : normalized;
    }

    private static string DescribeRuleChanges(LoyaltyRules before, LoyaltyRules after)
    {
        var changes = new List<string>();

        void Compare(string label, object oldValue, object newValue)
        {
            if (!Equals(oldValue, newValue))
                changes.Add($"{label}: {oldValue} → {newValue}");
        }

        Compare("Lei/punct", before.LeiPerPoint, after.LeiPerPoint);
        Compare("Prag minim bon", before.MinPurchaseForPoints, after.MinPurchaseForPoints);
        Compare("Valoare punct", before.PointValueLei, after.PointValueLei);
        Compare("Prag revendicare", before.MinimumRedeemablePoints, after.MinimumRedeemablePoints);
        Compare("Valabilitate puncte", before.PointsValidityMonths, after.PointsValidityMonths);
        Compare("Avertizare expirare", before.ExpiryWarningDays, after.ExpiryWarningDays);
        Compare("Bonus înscriere", before.WelcomeBonusPoints, after.WelcomeBonusPoints);
        Compare("Bonus aniversare", before.BirthdayBonusPoints, after.BirthdayBonusPoints);
        Compare("Fereastră aniversare", before.BirthdayWindowDays, after.BirthdayWindowDays);
        Compare("Perioadă calificare", before.QualifyingPeriodMonths, after.QualifyingPeriodMonths);
        Compare("Nume magazin", before.StoreName, after.StoreName);
        Compare("Nume program", before.ProgramName, after.ProgramName);

        return changes.Count == 0 ? "Fără modificări efective." : string.Join("; ", changes);
    }

    private static string DescribeCustomerChanges(Customer before, Customer after)
    {
        var changes = new List<string>();

        void Compare(string label, object? oldValue, object? newValue)
        {
            var o = oldValue?.ToString() ?? "-";
            var n = newValue?.ToString() ?? "-";
            if (o != n)
                changes.Add($"{label}: {o} → {n}");
        }

        Compare("Nume", before.LastName, after.LastName);
        Compare("Prenume", before.FirstName, after.FirstName);
        Compare("Telefon", before.Phone, after.Phone);
        Compare("E-mail", before.Email, after.Email);
        Compare("Card", before.CardCode, after.CardCode);
        Compare("Data nașterii", before.BirthDate?.ToString("dd.MM.yyyy"), after.BirthDate?.ToString("dd.MM.yyyy"));
        Compare("Activ", before.IsActive, after.IsActive);
        Compare("Acord marketing", before.MarketingConsent, after.MarketingConsent);

        return changes.Count == 0 ? "Fără modificări efective." : string.Join("; ", changes);
    }
}
