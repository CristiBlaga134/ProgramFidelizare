using System.Windows;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;

namespace Fidelizare.App.Views;

/// <summary>Resetarea parolei altui utilizator, de către administrator.</summary>
public partial class ResetPasswordWindow : Window
{
    private readonly AppUser _user;

    public ResetPasswordWindow(AppUser user)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _user = user;
        ContextText.Text = $"Se stabilește o parolă nouă pentru {user.Username}.";

        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        if (PasswordBox.Password != ConfirmBox.Password)
        {
            ShowError("Cele două parole nu coincid.");
            ConfirmBox.Clear();
            ConfirmBox.Focus();
            return;
        }

        var services = AppServices.Current;
        var result = services.Auth.ResetPassword(_user.Id, PasswordBox.Password, services.RequireUser);

        if (result.Failed)
        {
            ShowError(result.Error!);
            return;
        }

        Dialogs.Info($"Parola pentru {_user.Username} a fost resetată. " +
                     "Comunic-o utilizatorului — va fi obligat să o schimbe la prima autentificare.");

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }
}
