using System.Windows;
using System.Windows.Input;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.App.Views;

/// <summary>
/// Returul unei părți dintr-un bon. Se deschide fie din fișa clientului, cu bonul
/// deja ales, fie de la casă, unde clientul vine cu bonul în mână și se caută
/// după numărul lui.
///
/// Programul nu știe ce produse au fost pe bon, deci se introduce valoarea celor
/// returnate, așa cum apare pe documentul de retur al casei de marcat.
/// </summary>
public partial class ReturnWindow : Window
{
    private Purchase? _purchase;

    public ReturnWindow(Purchase? purchase = null)
    {
        InitializeComponent();
        this.FitToWorkArea();

        if (purchase is not null)
        {
            ReceiptPanel.Visibility = Visibility.Collapsed;
            ShowPurchase(purchase);
            Loaded += (_, _) => AmountBox.Focus();
        }
        else
        {
            Loaded += (_, _) => ReceiptBox.Focus();
        }
    }

    /// <summary>Clientul bonului returnat, ca ecranul de casă să-l poată afișa după retur.</summary>
    public int? CustomerId => _purchase?.CustomerId;

    private void OnReceiptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        FindReceipt();
        e.Handled = true;
    }

    private void OnFindReceipt(object sender, RoutedEventArgs e) => FindReceipt();

    private void FindReceipt()
    {
        HideError();

        var text = ReceiptBox.Text.Trim();
        if (text.Length == 0)
        {
            ShowError("Introdu numărul bonului.");
            return;
        }

        var purchase = AppServices.Current.Loyalty.FindPurchaseByReceipt(text);
        if (purchase is null)
        {
            ClearPurchase();
            ShowError($"Nu există nicio vânzare înregistrată cu bonul {text}. " +
                      "Dacă bonul nu a fost trecut la vânzare, returul se face din fișa clientului.");
            return;
        }

        ShowPurchase(purchase);
        AmountBox.Focus();
    }

    private void ShowPurchase(Purchase purchase)
    {
        _purchase = purchase;

        var customer = AppServices.Current.Loyalty.GetCustomer(purchase.CustomerId);

        CustomerText.Text = customer is null
            ? "Client necunoscut"
            : $"{customer.FullName} · card {customer.CardCode}";

        PurchaseText.Text =
            $"Bon {purchase.ReceiptNumber ?? "fără număr"} din {purchase.OccurredAt:dd.MM.yyyy HH:mm}: " +
            $"{purchase.NetAmount:N2} lei încasați, {purchase.PointsEarned} puncte acordate.";

        ReturnedText.Text = purchase.HasReturns
            ? $"Returnat deja: {purchase.ReturnedAmount:N2} lei ({purchase.PointsReturned} puncte). " +
              $"Mai pot fi returnați {purchase.RemainingAmount:N2} lei."
            : string.Empty;

        PurchaseCard.Visibility = Visibility.Visible;

        var blocker = purchase.IsVoided
            ? purchase.HasReturns && purchase.RemainingAmount == 0
                ? "Bonul a fost deja returnat integral."
                : "Vânzarea este anulată, nu se mai poate face retur pe ea."
            : null;

        if (blocker is not null)
        {
            ReturnPanel.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = false;
            ShowError(blocker);
            return;
        }

        ReturnPanel.Visibility = Visibility.Visible;
        ApplyButton.IsEnabled = true;
        UpdatePreview();
    }

    private void ClearPurchase()
    {
        _purchase = null;
        PurchaseCard.Visibility = Visibility.Collapsed;
        ReturnPanel.Visibility = Visibility.Collapsed;
        ApplyButton.IsEnabled = false;
    }

    private void OnReturnAll(object sender, RoutedEventArgs e)
    {
        if (_purchase is null)
            return;

        AmountBox.Text = AmountParser.Format(_purchase.RemainingAmount);
        ReasonBox.Focus();
    }

    private void OnAmountChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    /// <summary>Arată dinainte câte puncte se vor retrage, ca responsabilul să poată explica clientului.</summary>
    private void UpdatePreview()
    {
        if (_purchase is null || PreviewText is null)
            return;

        if (!AmountParser.TryParse(AmountBox.Text, out var amount) || amount <= 0)
        {
            PreviewText.Text = $"Maximum {_purchase.RemainingAmount:N2} lei.";
            return;
        }

        if (amount > _purchase.RemainingAmount)
        {
            PreviewText.Text = $"Suma depășește ce a rămas din bon ({_purchase.RemainingAmount:N2} lei).";
            return;
        }

        var points = PointsCalculator.PointsToReverseForReturn(_purchase, amount);
        PreviewText.Text = points == 0
            ? "Nu se retrage niciun punct."
            : $"Se vor retrage {points} puncte.";
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        HideError();

        if (_purchase is null)
            return;

        if (!AmountParser.TryParse(AmountBox.Text, out var amount) || amount <= 0)
        {
            ShowError("Introdu valoarea produselor returnate.");
            AmountBox.Focus();
            return;
        }

        var services = AppServices.Current;
        var result = services.Loyalty.ReturnPurchase(new ReturnRequest
        {
            PurchaseId = _purchase.Id,
            Amount = amount,
            Reason = ReasonBox.Text,
        }, services.RequireUser);

        if (result.Failed)
        {
            ShowError(result.Error!);
            return;
        }

        var outcome = result.Value!;
        var message =
            $"Returul de {outcome.Return.Amount:N2} lei a fost înregistrat. " +
            $"S-au retras {outcome.Return.PointsReversed - outcome.Return.PointsShortfall} puncte; " +
            $"clientul are acum {outcome.Summary.PointsBalance} puncte.";

        if (outcome.FullyReturned)
            message += "\n\nBonul a fost returnat integral.";

        if (outcome.Return.PointsShortfall > 0)
        {
            Dialogs.Warn(message + $"\n\n{outcome.Return.PointsShortfall} puncte fuseseră deja cheltuite " +
                         "și nu au mai putut fi retrase. Soldul a fost adus la zero.");
        }
        else
        {
            Dialogs.Info(message);
        }

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorBanner.Visibility = Visibility.Collapsed;
}
