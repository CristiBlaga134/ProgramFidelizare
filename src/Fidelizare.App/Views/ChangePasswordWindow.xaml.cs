using System.Windows;
using Fidelizare.App.Infrastructure;

namespace Fidelizare.App.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly bool _mandatory;

    public ChangePasswordWindow(bool mandatory = false)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _mandatory = mandatory;

        HintText.Text = mandatory
            ? "Parola contului a fost stabilită de administrator. Alege o parolă proprie pentru a continua."
            : "Alege o parolă nouă pentru contul tău.";

        // La schimbarea obligatorie nu există cale de ocolire: fereastra se închide
        // doar prin salvare sau prin renunțarea la autentificare.
        CancelButton.Content = mandatory ? "Renunță la autentificare" : "Renunță";

        Loaded += (_, _) => CurrentBox.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        if (NewBox.Password != ConfirmBox.Password)
        {
            ShowError("Cele două parole nu coincid.");
            ConfirmBox.Clear();
            ConfirmBox.Focus();
            return;
        }

        var services = AppServices.Current;
        var result = services.Auth.ChangePassword(services.RequireUser.Id, CurrentBox.Password, NewBox.Password);

        if (result.Failed)
        {
            ShowError(result.Error!);
            return;
        }

        services.RefreshCurrentUser();

        if (!_mandatory)
            Dialogs.Info("Parola a fost schimbată.");

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }
}
