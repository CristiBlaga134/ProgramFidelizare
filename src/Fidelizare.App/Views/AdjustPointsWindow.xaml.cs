using System.Windows;
using Fidelizare.App.Infrastructure;

namespace Fidelizare.App.Views;

/// <summary>
/// Corecție manuală de puncte. Necesită drepturi de responsabil și un motiv scris,
/// pentru că este singura cale prin care un sold se poate schimba fără o vânzare
/// sau o recompensă în spate.
/// </summary>
public partial class AdjustPointsWindow : Window
{
    private readonly int _customerId;

    public AdjustPointsWindow(int customerId, string customerName, int currentBalance)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _customerId = customerId;
        ContextText.Text = $"{customerName} are acum {currentBalance} puncte.";

        Loaded += (_, _) => PointsBox.Focus();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        if (!int.TryParse(PointsBox.Text.Trim(), out var points) || points <= 0)
        {
            ShowError("Introdu un număr de puncte mai mare decât zero.");
            PointsBox.Focus();
            return;
        }

        var signed = RemoveRadio.IsChecked == true ? -points : points;

        var services = AppServices.Current;
        var result = services.Loyalty.AdjustPoints(_customerId, signed, ReasonBox.Text, services.RequireUser);

        if (result.Failed)
        {
            ShowError(result.Error!);
            return;
        }

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }
}
