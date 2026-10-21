using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fidelizare.App.Infrastructure;
using Fidelizare.App.ViewModels;

namespace Fidelizare.App.Views;

/// <summary>
/// Ecranul de casă. Codul din spate se ocupă doar de lucrurile care țin de
/// tastatură și de ferestre; restul stă în modelul de vizualizare.
/// </summary>
public partial class PosView : UserControl, IRefreshable
{
    private readonly PosViewModel _model = new();

    public PosView()
    {
        InitializeComponent();
        DataContext = _model;

        // Câmpul de căutare primește focalizarea la intrarea pe ecran, ca un card
        // scanat să ajungă direct acolo fără să fie nevoie de mouse.
        Loaded += (_, _) =>
        {
            UpdatePermissions();
            SearchInput.Focus();
        };
    }

    public void Refresh()
    {
        UpdatePermissions();
        _model.ReloadRules();
        _model.ReloadCustomer();
        SearchInput.Focus();
    }

    /// <summary>
    /// Cititoarele de coduri de bare tastează conținutul cardului și trimit Enter.
    /// Tratarea tastei aici face ca scanarea să caute clientul fără niciun clic.
    /// </summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        _model.Search();
        e.Handled = true;

        // După o identificare reușită, cursorul trece direct pe suma bonului.
        if (_model.HasCustomer)
            GrossInput.Focus();
    }

    /// <summary>Enter în câmpurile bonului înregistrează vânzarea, ca la o casă de marcat.</summary>
    private void OnAmountKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (_model.RecordSaleCommand.CanExecute(null))
            _model.RecordSaleCommand.Execute(null);

        e.Handled = true;
    }

    /// <summary>Returul schimbă solduri, deci butonul apare doar celor care îl pot face.</summary>
    private void UpdatePermissions() =>
        ReturnButton.Visibility = AppServices.Current.CurrentUser?.CanManagePoints == true
            ?Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Clientul vine cu bonul: returul se caută după numărul lui, iar apoi clientul apare pe ecran.</summary>
    private void OnReturn(object sender, RoutedEventArgs e)
    {
        var dialog = new ReturnWindow { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() != true || dialog.CustomerId is not { } customerId)
            return;

        _model.SelectCustomer(AppServices.Current.Loyalty.GetCustomer(customerId));
    }

    private void OnOpenStaffAccounts(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.ShowUserAccounts();

    private void OnNewCustomer(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomerWindow { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true && dialog.SavedCustomer is not null)
            _model.SelectCustomer(dialog.SavedCustomer);
    }

    private void OnOpenCustomer(object sender, RoutedEventArgs e)
    {
        if (_model.Summary is null)
            return;

        var dialog = new CustomerDetailWindow(_model.Summary.Customer.Id)
        {
            Owner = Window.GetWindow(this),
        };

        dialog.ShowDialog();

        // Fișa permite corecții de puncte și anulări, deci soldul poate fi diferit.
        _model.ReloadCustomer();
    }
}
