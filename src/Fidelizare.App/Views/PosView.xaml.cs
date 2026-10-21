using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        Loaded += (_, _) => SearchInput.Focus();
    }

    public void Refresh()
    {
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
