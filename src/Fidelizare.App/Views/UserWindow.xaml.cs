using System.Windows;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;

namespace Fidelizare.App.Views;

/// <summary>Crearea sau modificarea unui cont de utilizator.</summary>
public partial class UserWindow : Window
{
    private readonly AppUser? _existing;

    public UserWindow(AppUser? existing)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _existing = existing;

        if (existing is null)
        {
            TitleText.Text = "Cont nou";
        }
        else
        {
            TitleText.Text = $"Contul {existing.Username}";
            NameBox.Text = existing.Username;
            RoleBox.SelectedIndex = (int)existing.Role - 1;
            ActiveCheck.IsChecked = existing.IsActive;

            // Parola existentă nu se poate citi și nu se schimbă de aici;
            // pentru asta există butonul separat de resetare.
            PasswordPanel.Visibility = Visibility.Collapsed;
        }

        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        var services = AppServices.Current;

        var user = new AppUser
        {
            Id = _existing?.Id ?? 0,
            Username = NameBox.Text.Trim(),
            Role = (UserRole)(RoleBox.SelectedIndex + 1),
            IsActive = ActiveCheck.IsChecked == true,
        };

        if (_existing is null)
        {
            if (PasswordBox.Password != ConfirmBox.Password)
            {
                ShowError("Cele două parole nu coincid.");
                ConfirmBox.Clear();
                return;
            }

            var result = services.Auth.CreateUser(user, PasswordBox.Password, services.RequireUser);

            if (result.Failed)
            {
                ShowError(result.Error!);
                return;
            }
        }
        else
        {
            // Un administrator care își retrage singur drepturile ar rămâne blocat
            // în afara configurării, deci merită un avertisment explicit.
            if (_existing.Id == services.RequireUser.Id &&
                _existing.Role == UserRole.Admin &&
                user.Role != UserRole.Admin &&
                !Dialogs.ConfirmDestructive(
                    "Îți retragi propriile drepturi de administrator.\n\n" +
                    "După salvare nu vei mai putea modifica regulile programului sau conturile. " +
                    "Continui?"))
            {
                return;
            }

            var result = services.Auth.UpdateUser(user, services.RequireUser);

            if (result.Failed)
            {
                ShowError(result.Error!);
                return;
            }
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
