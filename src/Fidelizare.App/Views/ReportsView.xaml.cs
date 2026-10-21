using System.Windows;
using System.Windows.Controls;
using Fidelizare.App.Infrastructure;

namespace Fidelizare.App.Views;

/// <summary>Un nivel pregătit pentru afișare în bara de distribuție.</summary>
public sealed class TierShare
{
    public required string TierName { get; init; }
    public required string ColorHex { get; init; }
    public int CustomerCount { get; init; }
    public double SharePercent { get; init; }
    public required string Label { get; init; }
}

/// <summary>Tabloul de bord: starea programului și cifrele perioadei alese.</summary>
public partial class ReportsView : UserControl, IRefreshable
{
    public ReportsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        try
        {
            LoadOverview();
            LoadPeriod();
            LoadAudit();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Rapoartele nu au putut fi calculate: {ex.Message}");
        }
    }

    private void LoadOverview()
    {
        var overview = AppServices.Current.Reporting.GetOverview();

        TotalCustomersText.Text = overview.TotalCustomers.ToString();
        ActiveCustomersText.Text = $"{overview.ActiveCustomers} activi";

        RecentActiveText.Text = overview.RecentlyActiveCustomers.ToString();
        RecentShareText.Text = overview.TotalCustomers == 0
            ? "—"
            : $"{overview.RecentlyActiveCustomers * 100 / overview.TotalCustomers}% din total";

        OutstandingText.Text = overview.OutstandingPoints.ToString("N0");
        LiabilityText.Text = $"≈ {overview.OutstandingLiability:N2} lei de acordat în reduceri viitoare";

        ExpiringText.Text = overview.PointsExpiringSoon.ToString("N0");

        var total = Math.Max(1, overview.ByTier.Sum(t => t.CustomerCount));

        TierList.ItemsSource = overview.ByTier.Select(t => new TierShare
        {
            TierName = t.TierName,
            ColorHex = t.ColorHex,
            CustomerCount = t.CustomerCount,
            SharePercent = t.CustomerCount * 100d / total,
            Label = $"{t.CustomerCount} clienți · {t.TotalSpend:N0} lei",
        }).ToList();
    }

    private void LoadPeriod()
    {
        var services = AppServices.Current;
        var today = services.Clock.Today;

        var (from, to, title) = PeriodSelector.SelectedIndex switch
        {
            1 => (today.AddDays(-6), today.AddDays(1), "Ultimele 7 zile"),
            2 => (today.AddDays(-29), today.AddDays(1), "Ultimele 30 de zile"),
            3 => (new DateTime(today.Year, today.Month, 1), today.AddDays(1), "Luna curentă"),
            4 => (new DateTime(today.Year, 1, 1), today.AddDays(1), "Anul curent"),
            _ => (today, today.AddDays(1), "Astăzi"),
        };

        PeriodTitle.Text = from.Date == to.Date.AddDays(-1)
            ? $"{title} — {from:dd.MM.yyyy}"
            : $"{title} — {from:dd.MM.yyyy} … {to.AddDays(-1):dd.MM.yyyy}";

        var report = services.Reporting.GetPeriodReport(from, to);

        NetSalesText.Text = $"{report.NetSales:N2}";
        GrossSalesText.Text = report.DiscountsGiven > 0
            ? $"din care {report.DiscountsGiven:N2} lei reduceri"
            : "fără reduceri acordate";

        PurchaseCountText.Text = report.PurchaseCount.ToString();
        AverageBasketText.Text = report.PurchaseCount == 0
            ? "—"
            : $"bon mediu {report.AverageBasket:N2} lei · {report.ActiveCustomers} clienți";

        PointsIssuedText.Text = report.PointsIssued.ToString("N0");
        NewCustomersText.Text = report.NewCustomers == 1
            ? "un client nou înscris"
            : $"{report.NewCustomers} clienți noi înscriși";

        PointsRedeemedText.Text = report.PointsRedeemed.ToString("N0");
        RedemptionCountText.Text = report.RedemptionCount == 0
            ? "nicio recompensă acordată"
            : $"{report.RedemptionCount} recompense · {report.RewardDiscountValue:N2} lei";

        PointsExpiredText.Text = report.PointsExpired.ToString("N0");
        VoidedText.Text = report.VoidedPurchases == 0
            ? "nicio vânzare anulată"
            : $"{report.VoidedPurchases} vânzări anulate";

        var top = services.Reporting.GetTopCustomers(from, to, 15);
        TopGrid.ItemsSource = top;
        TopGrid.Visibility = top.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyTopText.Visibility = top.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadAudit() =>
        AuditGrid.ItemsSource = AppServices.Current.Reporting.GetAuditTrail(200);

    private void OnPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        // Evenimentul se declanșează și la construirea ferestrei, înainte ca
        // celelalte elemente să existe.
        if (!IsLoaded)
            return;

        try
        {
            LoadPeriod();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Raportul nu a putut fi calculat: {ex.Message}");
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();
}
