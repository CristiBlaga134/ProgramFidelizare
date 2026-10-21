using Fidelizare.Core.Model;

namespace Fidelizare.Core.Services;

/// <summary>
/// Calculele programului, izolate ca funcții pure: aceleași intrări dau
/// întotdeauna același rezultat, fără bază de date și fără ceas.
/// Aici se află răspunsul la „de ce am primit atâtea puncte”.
/// </summary>
public static class PointsCalculator
{
    /// <summary>
    /// Punctele pentru o cumpărare. Se calculează pe suma net încasată, astfel
    /// încât reducerea obținută din puncte să nu genereze puncte noi.
    ///
    /// Rotunjirea se face în jos, la fiecare pas. Un bon de 97 lei la 10 lei/punct
    /// dă 9 puncte, nu 10: clientul nu primește niciodată mai mult decât a cheltuit,
    /// iar regula este ușor de explicat la casă.
    /// </summary>
    public static int PointsForPurchase(decimal netAmount, decimal tierMultiplier, LoyaltyRules rules)
    {
        if (netAmount <= 0 || rules.LeiPerPoint <= 0)
            return 0;

        if (netAmount < rules.MinPurchaseForPoints)
            return 0;

        var basePoints = Math.Floor(netAmount / rules.LeiPerPoint);
        if (basePoints <= 0)
            return 0;

        var multiplier = tierMultiplier <= 0 ? 1m : tierMultiplier;
        var total = Math.Floor(basePoints * multiplier);

        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    /// <summary>
    /// Punctele de retras la returul unei sume dintr-un bon.
    ///
    /// Clientul păstrează punctele proporționale cu ce a rămas din bon, rotunjite
    /// în jos, ca la acordare; se retrage diferența față de ce fusese deja retras.
    /// Calculul pleacă de la punctele efectiv acordate, nu de la regulile de acum,
    /// deci multiplicatorul de nivel și campania din ziua vânzării rămân valabile
    /// chiar dacă între timp regulile s-au schimbat. Retururile succesive pe același
    /// bon ajung împreună exact la punctele acordate, niciodată mai mult.
    /// </summary>
    public static int PointsToReverseForReturn(Purchase purchase, decimal returnAmount)
    {
        if (returnAmount <= 0 || purchase.NetAmount <= 0 || purchase.PointsEarned <= 0)
            return 0;

        var remainingAfter = Math.Max(0m, purchase.RemainingAmount - returnAmount);
        var pointsKept = (int)Math.Floor(purchase.PointsEarned * remainingAfter / purchase.NetAmount);
        var totalReversed = purchase.PointsEarned - pointsKept;

        return Math.Max(0, totalReversed - purchase.PointsReturned);
    }

    /// <summary>
    /// Nivelul corespunzător unui rulaj. Nivelurile vin ordonate crescător;
    /// se alege ultimul al cărui prag este atins.
    /// </summary>
    public static Tier DetermineTier(decimal qualifyingSpend, IReadOnlyList<Tier> tiers)
    {
        if (tiers.Count == 0)
            throw new InvalidOperationException("Programul nu are niciun nivel definit.");

        var ordered = tiers.OrderBy(t => t.Threshold).ThenBy(t => t.SortOrder).ToList();

        var result = ordered[0];
        foreach (var tier in ordered)
        {
            if (qualifyingSpend >= tier.Threshold)
                result = tier;
            else
                break;
        }

        return result;
    }

    /// <summary>Nivelul imediat superior, sau null dacă acesta este cel mai înalt.</summary>
    public static Tier? GetNextTier(Tier current, IReadOnlyList<Tier> tiers) =>
        tiers.OrderBy(t => t.Threshold)
             .ThenBy(t => t.SortOrder)
             .FirstOrDefault(t => t.Threshold > current.Threshold);

    /// <summary>
    /// Valoarea în lei a unei recompense aplicate pe un coș dat.
    /// Reducerea nu poate depăși valoarea coșului: nu se dau bani înapoi.
    /// </summary>
    public static decimal CalculateRewardDiscount(Reward reward, decimal basketAmount)
    {
        if (basketAmount <= 0)
            return 0m;

        var discount = reward.Kind switch
        {
            RewardKind.FixedAmountDiscount => reward.Value,
            RewardKind.PercentageDiscount => ApplyCap(basketAmount * reward.Value / 100m, reward.MaxDiscountAmount),
            RewardKind.FreeProduct => reward.Value,
            _ => 0m,
        };

        discount = Math.Min(discount, basketAmount);
        return Math.Round(Math.Max(discount, 0m), 2, MidpointRounding.AwayFromZero);

        static decimal ApplyCap(decimal value, decimal cap) => cap > 0 ? Math.Min(value, cap) : value;
    }

    /// <summary>Motivul pentru care o recompensă nu poate fi folosită acum, sau null dacă poate.</summary>
    public static string? GetRedemptionBlocker(
        Reward reward,
        int pointsBalance,
        decimal basketAmount,
        Tier customerTier,
        IReadOnlyList<Tier> allTiers,
        LoyaltyRules rules,
        DateTime asOf)
    {
        if (!reward.IsActive)
            return "Recompensa nu este activă.";

        if (reward.ValidFrom is { } from && asOf.Date < from.Date)
            return $"Recompensa devine valabilă pe {from:dd.MM.yyyy}.";

        if (reward.ValidTo is { } to && asOf.Date > to.Date)
            return $"Recompensa a expirat pe {to:dd.MM.yyyy}.";

        if (pointsBalance < rules.MinimumRedeemablePoints)
            return $"Sunt necesare cel puțin {rules.MinimumRedeemablePoints} puncte pentru orice revendicare.";

        if (pointsBalance < reward.PointsCost)
            return $"Mai sunt necesare {reward.PointsCost - pointsBalance} puncte.";

        if (reward.MinTierId is { } minTierId)
        {
            var required = allTiers.FirstOrDefault(t => t.Id == minTierId);
            if (required is not null && customerTier.Threshold < required.Threshold)
                return $"Recompensa este rezervată nivelului {required.Name}.";
        }

        if (reward.MinPurchaseAmount > 0 && basketAmount < reward.MinPurchaseAmount)
            return $"Valoarea minimă a bonului este {reward.MinPurchaseAmount:0.##} lei.";

        return null;
    }

    /// <summary>
    /// Verifică dacă bonusul de zi de naștere poate fi acordat acum: clientul are
    /// data completată, este în fereastra din jurul zilei și nu l-a primit anul acesta.
    /// </summary>
    public static bool IsBirthdayBonusDue(Customer customer, LoyaltyRules rules, DateTime asOf)
    {
        if (rules.BirthdayBonusPoints <= 0 || customer.BirthDate is not { } birthDate)
            return false;

        if (customer.LastBirthdayBonusYear == asOf.Year)
            return false;

        return IsWithinBirthdayWindow(birthDate, asOf, rules.BirthdayWindowDays);
    }

    /// <summary>
    /// Verifică dacă data curentă cade în fereastra din jurul aniversării.
    /// Se compară cu aniversarea din anul curent, dar și cu cea din anii vecini,
    /// pentru cazul în care fereastra trece peste 1 ianuarie.
    /// </summary>
    public static bool IsWithinBirthdayWindow(DateTime birthDate, DateTime asOf, int windowDays)
    {
        var today = asOf.Date;

        foreach (var year in new[] { today.Year - 1, today.Year, today.Year + 1 })
        {
            var anniversary = AnniversaryIn(birthDate, year);
            if (Math.Abs((today - anniversary).TotalDays) <= windowDays)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Aniversarea într-un an dat. Pentru cei născuți pe 29 februarie, în anii
    /// nebisecți aniversarea se consideră pe 28 februarie.
    /// </summary>
    public static DateTime AnniversaryIn(DateTime birthDate, int year)
    {
        var day = birthDate.Day;
        var month = birthDate.Month;

        if (month == 2 && day == 29 && !DateTime.IsLeapYear(year))
            day = 28;

        return new DateTime(year, month, day);
    }

    /// <summary>Valoarea orientativă în lei a unui sold de puncte, pentru afișare.</summary>
    public static decimal EstimateValue(int points, LoyaltyRules rules) =>
        Math.Round(points * rules.PointValueLei, 2, MidpointRounding.AwayFromZero);
}
