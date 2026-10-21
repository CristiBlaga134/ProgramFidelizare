using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.App.ViewModels;

/// <summary>
/// Un rând din lista de clienți: datele clientului plus valorile calculate de
/// care are nevoie tabelul.
///
/// Nivelul și soldul nu sunt câmpuri în baza de date — se deduc din cumpărări și
/// din loturile de puncte — deci se calculează aici, o singură dată la încărcarea
/// listei, în loc să fie cerute de fiecare celulă în parte.
/// </summary>
public sealed class CustomerRow
{
    public required Customer Customer { get; init; }

    public int Points { get; init; }

    public required string TierName { get; init; }

    public required string TierColor { get; init; }

    public decimal QualifyingValue { get; init; }

    public decimal LifetimeSpend { get; init; }

    public DateTime? LastPurchaseAt { get; init; }

    public int PurchaseCount { get; init; }

    public string FullName => Customer.FullName;
    public string CardCode => Customer.CardCode;
    public string? Phone => Customer.Phone;
    public bool IsActive => Customer.IsActive;

    public string LastPurchaseLabel => LastPurchaseAt?.ToString("dd.MM.yyyy") ?? "—";

    /// <summary>
    /// Încarcă lista completă într-o singură unitate de lucru. Deschiderea unei
    /// unități per client ar însemna o tranzacție pentru fiecare rând.
    /// </summary>
    public static List<CustomerRow> Load(IUnitOfWorkFactory factory, IClock clock, CustomerQuery query)
    {
        using var uow = factory.Create();

        var rules = LoyaltyRules.Load(uow.Settings);
        var tiers = uow.Tiers.GetAll();
        var qualifyingStart = rules.GetQualifyingPeriodStart(clock.Now);

        var rows = new List<CustomerRow>();

        foreach (var customer in uow.Customers.Search(query))
        {
            var qualifying = uow.Purchases.GetNetSpendSince(customer.Id, qualifyingStart);
            var tier = PointsCalculator.DetermineTier(qualifying, tiers);

            rows.Add(new CustomerRow
            {
                Customer = customer,
                Points = uow.Points.GetBalance(customer.Id),
                TierName = tier.Name,
                TierColor = tier.ColorHex,
                QualifyingValue = qualifying,
                LifetimeSpend = uow.Purchases.GetLifetimeSpend(customer.Id),
                LastPurchaseAt = uow.Purchases.GetLastPurchaseDate(customer.Id),
                PurchaseCount = uow.Purchases.GetPurchaseCount(customer.Id),
            });
        }

        return rows;
    }
}
