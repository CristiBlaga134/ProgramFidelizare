using System.Windows;
using Fidelizare.App.Infrastructure;

namespace Fidelizare.App.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        this.FitToWorkArea();

        var rules = AppServices.Current.Loyalty.GetRules();
        ProgramNameText.Text = rules.ProgramName;
        StoreNameText.Text = rules.StoreName;

        var logo = Branding.LoadLogo();
        if (logo is not null)
        {
            LogoImage.Source = logo;
            LogoImage.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void OnLogin(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        var services = AppServices.Current;
        var result = services.Auth.Login(UsernameBox.Text, PasswordBox.Password);

        if (result.Failed)
        {
            ErrorText.Text = result.Error;
            ErrorBanner.Visibility = Visibility.Visible;
            PasswordBox.Clear();
            PasswordBox.Focus();
            return;
        }

        var user = result.Value!;
        services.SignIn(user);

        // Un cont cu parolă resetată de administrator nu poate lucra până nu își
        // pune o parolă proprie: altfel cel care a resetat-o ar putea opera în numele lui.
        if (user.MustChangePassword)
        {
            var change = new ChangePasswordWindow(mandatory: true) { Owner = this };

            if (change.ShowDialog() != true)
            {
                services.SignOut();
                ErrorText.Text = "Schimbarea parolei este obligatorie la prima autentificare.";
                ErrorBanner.Visibility = Visibility.Visible;
                PasswordBox.Clear();
                return;
            }

            services.RefreshCurrentUser();
        }

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
