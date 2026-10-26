using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;

namespace Fidelizare.Core.Services;

/// <summary>Cifrele unei perioade, așa cum le-ar cere patronul magazinului.</summary>
public sealed class PeriodReport
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }

    public int PurchaseCount { get; init; }
    public decimal GrossSales { get; init; }
    public decimal NetSales { get; init; }
    public decimal DiscountsGiven { get; init; }

    /// <summary>Valoarea medie a unui bon, pe vânzările neanulate.</summary>
    public decimal AverageBasket => PurchaseCount == 0 ? 0m : Math.Round(NetSales / PurchaseCount, 2);

    public int PointsIssued { get; init; }
    public int PointsRedeemed { get; init; }
    public int PointsExpired { get; init; }

    public int RedemptionCount { get; init; }

    /// <summary>Reducerile acordate prin recompense, incluse deja în DiscountsGiven.</summary>
    public decimal RewardDiscountValue { get; init; }

    public int NewCustomers { get; init; }

    /// <summary>Câți clienți distincți au cumpărat în perioadă.</summary>
    public int ActiveCustomers { get; init; }

    public int VoidedPurchases { get; init; }
}

/// <summary>Starea programului la un moment dat.</summary>
public sealed class ProgramOverview
{
    public int TotalCustomers { get; init; }
    public int ActiveCustomers { get; init; }

    /// <summary>Câți clienți au cumpărat în ultimele 90 de zile.</summary>
    public int RecentlyActiveCustomers { get; init; }

    /// <summary>Punctele nefolosite din tot programul.</summary>
    public int OutstandingPoints { get; init; }

    /// <summary>
    /// Valoarea în lei a punctelor nefolosite. Este suma pe care magazinul o
    /// datorează clienților sub formă de reduceri viitoare, deci merită urmărită
    /// la fel ca stocul: dacă crește necontrolat, regulile sunt prea generoase.
    /// </summary>
    public decimal OutstandingLiability { get; init; }

    /// <summary>Puncte care expiră în următoarele 30 de zile, pe tot programul.</summary>
    public int PointsExpiringSoon { get; init; }

    public IReadOnlyList<TierBreakdown> ByTier { get; init; } = [];
}

public sealed class TierBreakdown
{
    public required string TierName { get; init; }
    public required string ColorHex { get; init; }
    public int CustomerCount { get; init; }
    public decimal TotalSpend { get; init; }
}

public sealed class TopCustomerRow
{
    public required Customer Customer { get; init; }
    public decimal Spend { get; init; }
    public int PurchaseCount { get; init; }
    public required string TierName { get; init; }
}

/// <summary>
/// Citește cifrele programului. Nu modifică nimic, deci toate metodele deschid o
/// unitate de lucru pe care nu o confirmă.
/// </summary>
public sealed class ReportingService
{
    private readonly IUnitOfWorkFactory _factory;
    private readonly IClock _clock;

    public ReportingService(IUnitOfWorkFactory factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    /// <summary>
    /// Raport pe un interval. Capătul „până la” este exclusiv, deci o zi se cere ca [zi, zi+1).
    /// </summary>
    /// <param name="userId">
    /// Dacă e dat, doar operațiunile făcute de acel utilizator: casierul își vede
    /// propriile vânzări, nu încasările magazinului. Expirările nu au autor, deci
    /// în acest caz rămân zero.
    /// </param>
    public PeriodReport GetPeriodReport(DateTime fromInclusive, DateTime toExclusive, int? userId = null)
    {
        using var uow = _factory.Create();

        var purchases = uow.Purchases.GetBetween(fromInclusive, toExclusive)
            .Where(p => userId is null || p.RecordedByUserId == userId)
            .ToList();
        var valid = purchases.Where(p => !p.IsVoided).ToList();

        var transactions = uow.Points.GetTransactionsBetween(fromInclusive, toExclusive)
            .Where(t => userId is null || (t.UserId == userId && t.Type != PointTransactionType.Expired))
            .ToList();
        var redemptions = uow.Redemptions.GetBetween(fromInclusive, toExclusive)
            .Where(r => !r.IsVoided && (userId is null || r.UserId == userId))
            .ToList();

        // Înscrierea nu are autor pe client, ci doar în jurnal.
        var newCustomers = userId is null
            ? uow.Customers
                .Search(new CustomerQuery { IncludeInactive = true, Take = int.MaxValue })
                .Count(c => c.EnrolledAt >= fromInclusive && c.EnrolledAt < toExclusive)
            : uow.Audit.GetBetween(fromInclusive, toExclusive)
                .Count(a => a.Action == "CustomerEnrolled" && a.UserId == userId);

        return new PeriodReport
        {
            From = fromInclusive,
            To = toExclusive,
            PurchaseCount = valid.Count,
            GrossSales = valid.Sum(p => p.GrossAmount),
            NetSales = valid.Sum(p => p.RemainingAmount),
            DiscountsGiven = valid.Sum(p => p.DiscountAmount),
            VoidedPurchases = purchases.Count - valid.Count,
            ActiveCustomers = valid.Select(p => p.CustomerId).Distinct().Count(),

            // Punctele acordate includ bonusurile, nu doar cele din cumpărături.
            PointsIssued = transactions.Where(t => t.Points > 0).Sum(t => t.Points),
            PointsRedeemed = transactions
                .Where(t => t.Type == PointTransactionType.Redeemed)
                .Sum(t => -t.Points),
            PointsExpired = transactions
                .Where(t => t.Type == PointTransactionType.Expired)
                .Sum(t => -t.Points),

            RedemptionCount = redemptions.Count,
            RewardDiscountValue = redemptions.Sum(r => r.DiscountValue),
            NewCustomers = newCustomers,
        };
    }

    public PeriodReport GetToday() => GetPeriodReport(_clock.Today, _clock.Today.AddDays(1));

    public PeriodReport GetCurrentMonth()
    {
        var start = new DateTime(_clock.Today.Year, _clock.Today.Month, 1);
        return GetPeriodReport(start, start.AddMonths(1));
    }

    /// <summary>
    /// Starea generală a programului. Parcurge toți clienții, deci este gândită
    /// pentru un magazin, nu pentru un lanț cu sute de mii de clienți.
    /// </summary>
    public ProgramOverview GetOverview()
    {
        using var uow = _factory.Create();

        var rules = LoyaltyRules.Load(uow.Settings);
        var tiers = uow.Tiers.GetAll();
        var now = _clock.Now;
        var qualifyingStart = rules.GetQualifyingPeriodStart(now);
        var recentThreshold = now.AddDays(-90);

        var customers = uow.Customers.Search(new CustomerQuery { IncludeInactive = true, Take = int.MaxValue });

        var outstanding = 0;
        var expiringSoon = 0;
        var recentlyActive = 0;
        var active = 0;

        var counts = new Dictionary<string, (int Count, decimal Spend)>(StringComparer.Ordinal);
        foreach (var tier in tiers)
            counts[tier.Name] = (0, 0m);

        foreach (var customer in customers)
        {
            if (customer.IsActive)
                active++;

            outstanding += uow.Points.GetBalance(customer.Id);
            expiringSoon += uow.Points.GetPointsExpiringBefore(customer.Id, now.Date.AddDays(31));

            var spend = uow.Purchases.GetNetSpendSince(customer.Id, qualifyingStart);
            var tier = PointsCalculator.DetermineTier(spend, tiers);

            var current = counts[tier.Name];
            counts[tier.Name] = (current.Count + 1, current.Spend + spend);

            var last = uow.Purchases.GetLastPurchaseDate(customer.Id);
            if (last is not null && last >= recentThreshold)
                recentlyActive++;
        }

        return new ProgramOverview
        {
            TotalCustomers = customers.Count,
            ActiveCustomers = active,
            RecentlyActiveCustomers = recentlyActive,
            OutstandingPoints = outstanding,
            OutstandingLiability = PointsCalculator.EstimateValue(outstanding, rules),
            PointsExpiringSoon = expiringSoon,
            ByTier = tiers.Select(t => new TierBreakdown
            {
                TierName = t.Name,
                ColorHex = t.ColorHex,
                CustomerCount = counts[t.Name].Count,
                TotalSpend = counts[t.Name].Spend,
            }).ToList(),
        };
    }

    /// <summary>Clienții cu cel mai mare rulaj din perioada dată.</summary>
    public IReadOnlyList<TopCustomerRow> GetTopCustomers(DateTime fromInclusive, DateTime toExclusive, int take = 20)
    {
        using var uow = _factory.Create();

        var rules = LoyaltyRules.Load(uow.Settings);
        var tiers = uow.Tiers.GetAll();
        var qualifyingStart = rules.GetQualifyingPeriodStart(_clock.Now);

        var purchases = uow.Purchases.GetBetween(fromInclusive, toExclusive)
            .Where(p => !p.IsVoided)
            .GroupBy(p => p.CustomerId)
            .Select(g => new { CustomerId = g.Key, Spend = g.Sum(p => p.RemainingAmount), Count = g.Count() })
            .OrderByDescending(x => x.Spend)
            .Take(take)
            .ToList();

        var rows = new List<TopCustomerRow>(purchases.Count);

        foreach (var entry in purchases)
        {
            var customer = uow.Customers.GetById(entry.CustomerId);
            if (customer is null)
                continue;

            var qualifying = uow.Purchases.GetNetSpendSince(customer.Id, qualifyingStart);

            rows.Add(new TopCustomerRow
            {
                Customer = customer,
                Spend = entry.Spend,
                PurchaseCount = entry.Count,
                TierName = PointsCalculator.DetermineTier(qualifying, tiers).Name,
            });
        }

        return rows;
    }

    /// <summary>Vânzările zilnice dintr-un interval, pentru graficul din tabloul de bord.</summary>
    public IReadOnlyList<(DateTime Day, decimal Net, int Purchases)> GetDailySales(DateTime fromInclusive, DateTime toExclusive)
    {
        using var uow = _factory.Create();

        var byDay = uow.Purchases.GetBetween(fromInclusive, toExclusive)
            .Where(p => !p.IsVoided)
            .GroupBy(p => p.OccurredAt.Date)
            .ToDictionary(g => g.Key, g => (Net: g.Sum(p => p.RemainingAmount), Count: g.Count()));

        var result = new List<(DateTime, decimal, int)>();

        // Zilele fără vânzări trebuie să apară cu zero, altfel graficul comprimă
        // perioadele slabe și dă o imagine falsă.
        for (var day = fromInclusive.Date; day < toExclusive.Date; day = day.AddDays(1))
        {
            result.Add(byDay.TryGetValue(day, out var value)
                ? (day, value.Net, value.Count)
                : (day, 0m, 0));
        }

        return result;
    }

    public IReadOnlyList<AuditEntry> GetAuditTrail(int take = 500)
    {
        using var uow = _factory.Create();
        return uow.Audit.GetRecent(take);
    }
}
