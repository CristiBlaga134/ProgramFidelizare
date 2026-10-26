using System.Windows;
using System.Windows.Media;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.App.Views;

/// <summary>
/// Fișa completă a unui client: situația curentă, cumpărările, recompensele
/// primite și fiecare mișcare de puncte.
///
/// Este ecranul la care se ajunge când un client întreabă „de ce am atâtea
/// puncte”, deci trebuie să arate întregul drum, nu doar soldul.
/// </summary>
public partial class CustomerDetailWindow : Window
{
    private readonly int _customerId;
    private CustomerSummary? _summary;

    public CustomerDetailWindow(int customerId)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _customerId = customerId;

        var user = AppServices.Current.RequireUser;

        // Operațiunile care schimbă solduri sunt vizibile doar celor care le pot face.
        var visibility = user.CanManagePoints ? Visibility.Visible : Visibility.Collapsed;
        AdjustButton.Visibility = visibility;
        VoidPurchaseButton.Visibility = visibility;
        VoidRedemptionButton.Visibility = visibility;

        Load();
    }

    /// <summary>Adevărat dacă s-a făcut vreo modificare, ca ecranul apelant să se reîmprospăteze.</summary>
    public bool DataChanged { get; private set; }

    private void Load()
    {
        var services = AppServices.Current;

        _summary = services.Loyalty.GetSummary(_customerId);

        if (_summary is null)
        {
            Dialogs.Error("Clientul nu a fost găsit.");
            Close();
            return;
        }

        var rules = services.Loyalty.GetRules();
        var customer = _summary.Customer;

        Title = $"Fișa clientului — {customer.FullName}";
        NameText.Text = customer.FullName;
        TierText.Text = _summary.Tier.Name;
        TierBadge.Background = BrushFromHex(_summary.Tier.ColorHex);
        InactiveBadge.Visibility = customer.IsActive ? Visibility.Collapsed : Visibility.Visible;

        var contact = new List<string> { $"Card {customer.CardCode}" };
        if (!string.IsNullOrWhiteSpace(customer.Phone))
            contact.Add(customer.Phone);
        if (!string.IsNullOrWhiteSpace(customer.Email))
            contact.Add(customer.Email);

        ContactText.Text = string.Join("  ·  ", contact);
        EnrolledText.Text = $"Înscris pe {customer.EnrolledAt:dd.MM.yyyy}" +
                            (customer.MarketingConsent ? " · acord pentru comunicări comerciale" : string.Empty);

        PointsText.Text = _summary.PointsBalance.ToString();
        PointsValueText.Text = $"≈ {PointsCalculator.EstimateValue(_summary.PointsBalance, rules):N2} lei";

        QualifyingText.Text = $"{_summary.QualifyingValue:N2} lei";
        NextTierText.Text = _summary.NextTier is null
            ? "Nivel maxim atins."
            : $"Încă {_summary.ValueToNextTier:N2} lei până la {_summary.NextTier.Name}.";

        LifetimeText.Text = $"{_summary.LifetimeSpend:N2} lei";
        PurchaseCountText.Text = $"{_summary.PurchaseCount} cumpărări";

        LastPurchaseText.Text = _summary.LastPurchaseAt is { } last
            ? last.ToString("dd.MM.yyyy")
            : "—";

        BirthdayText.Text = customer.BirthDate is { } birth
            ? $"Ziua de naștere: {birth:dd.MM}"
            : "Fără dată de naștere";

        ExpiringText.Text = _summary.PointsExpiringSoon > 0 ? _summary.PointsExpiringSoon.ToString() : "—";
        ExpiryDateText.Text = _summary.NextExpiryAt is { } expiry
            ? $"Primul lot expiră pe {expiry:dd.MM.yyyy}"
            : "Niciun lot aproape de expirare";

        using var uow = services.Factory.Create();
        PurchasesGrid.ItemsSource = uow.Purchases.GetForCustomer(_customerId, 300);
        RedemptionsGrid.ItemsSource = uow.Redemptions.GetForCustomer(_customerId, 300);
        LedgerGrid.ItemsSource = uow.Points.GetTransactions(_customerId, 500);
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_summary is null)
            return;

        var dialog = new CustomerWindow(_summary.Customer) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            DataChanged = true;
            Load();
        }
    }

    private void OnAdjustPoints(object sender, RoutedEventArgs e)
    {
        if (_summary is null)
            return;

        var dialog = new AdjustPointsWindow(_customerId, _summary.Customer.FullName, _summary.PointsBalance)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            DataChanged = true;
            Load();
        }
    }

    private void OnReturn(object sender, RoutedEventArgs e)
    {
        if (PurchasesGrid.SelectedItem is not Purchase purchase)
        {
            Dialogs.Info("Selectează întâi cumpărarea din care se face returul, din fila „Cumpărări”.");
            return;
        }

        var dialog = new ReturnWindow(purchase) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            DataChanged = true;
            Load();
        }
    }

    private void OnVoidPurchase(object sender, RoutedEventArgs e)
    {
        if (PurchasesGrid.SelectedItem is not Purchase purchase)
        {
            Dialogs.Info("Selectează întâi cumpărarea pe care vrei să o anulezi, din fila „Cumpărări”.");
            return;
        }

        if (purchase.IsVoided)
        {
            Dialogs.Info("Cumpărarea este deja anulată.");
            return;
        }

        var prompt = new PromptWindow(
            "Anularea unei cumpărări",
            $"Cumpărare din {purchase.OccurredAt:dd.MM.yyyy}, {purchase.NetAmount:N2} lei, " +
            $"{purchase.PointsEarned} puncte acordate" +
            (purchase.HasReturns ? $", din care {purchase.PointsReturned} retrase deja prin retur" : "") + ".\n\n" +
            "Punctele acordate vor fi retrase. Dacă între timp au fost cheltuite, " +
            "soldul va fi adus la zero fără a deveni negativ.",
            "MOTIVUL ANULĂRII",
            "Anulează cumpărarea")
        {
            Owner = this,
        };

        if (prompt.ShowDialog() != true)
            return;

        var services = AppServices.Current;
        var result = services.Loyalty.VoidPurchase(purchase.Id, prompt.Value, services.RequireUser);

        DataChanged = true;

        // Un eșec parțial este raportat ca avertisment: anularea s-a făcut, dar
        // nu toate punctele au putut fi retrase.
        if (result.Failed)
            Dialogs.Warn(result.Error!);
        else
            Dialogs.Info("Cumpărarea a fost anulată și punctele au fost retrase.");

        Load();
    }

    private void OnVoidRedemption(object sender, RoutedEventArgs e)
    {
        if (RedemptionsGrid.SelectedItem is not Redemption redemption)
        {
            Dialogs.Info("Selectează întâi recompensa pe care vrei să o anulezi, din fila „Recompense acordate”.");
            return;
        }

        if (redemption.IsVoided)
        {
            Dialogs.Info("Recompensa este deja anulată.");
            return;
        }

        var prompt = new PromptWindow(
            "Anularea unei recompense",
            $"„{redemption.RewardName}” din {redemption.OccurredAt:dd.MM.yyyy}, " +
            $"{redemption.PointsSpent} puncte.\n\n" +
            "Punctele se întorc în loturile din care au fost luate, cu datele lor originale de expirare.",
            "MOTIVUL ANULĂRII",
            "Anulează recompensa")
        {
            Owner = this,
        };

        if (prompt.ShowDialog() != true)
            return;

        var services = AppServices.Current;
        var result = services.Loyalty.VoidRedemption(redemption.Id, prompt.Value, services.RequireUser);

        DataChanged = true;

        if (result.Failed)
            Dialogs.Warn(result.Error!);
        else
            Dialogs.Info("Recompensa a fost anulată și punctele au fost restituite.");

        Load();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static Brush BrushFromHex(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (Exception)
        {
            return Brushes.Gray;
        }
    }
}
