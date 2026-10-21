using System.Windows;
using System.Windows.Controls;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;

namespace Fidelizare.App.Views;

/// <summary>
/// Fereastra principală: bara de navigare și zona de conținut.
///
/// Ecranele se construiesc la prima folosire și rămân apoi în memorie. Sunt
/// puține și ușoare, iar păstrarea lor face ca revenirea la un ecran să regăsească
/// exact ce era acolo — clientul căutat, sumele tastate — în loc să repornească
/// de la zero de fiecare dată.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _views = [];
    private bool _signingOut;

    public MainWindow()
    {
        InitializeComponent();
        this.FitToWorkArea();

        var services = AppServices.Current;
        var rules = services.Loyalty.GetRules();

        ProgramNameText.Text = rules.ProgramName;
        StoreNameText.Text = rules.StoreName;
        Title = $"{rules.ProgramName} — {rules.StoreName}";

        // Logo-ul este opțional: fără el rămân numele programului și al magazinului.
        var logo = Branding.LoadLogo();
        if (logo is not null)
        {
            LogoImage.Source = logo;
            LogoImage.Visibility = Visibility.Visible;
        }

        var user = services.RequireUser;
        UserNameText.Text = user.Username;
        UserRoleText.Text = user.RoleLabel;

        // Casierul nu are ce căuta în configurarea programului, deci intrarea
        // nici nu apare. Restricția este oricum aplicată și în servicii.
        NavAdmin.Visibility = user.Role is UserRole.Manager or UserRole.Admin
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Scurtătura către conturi are sens doar pentru cine le poate crea.
        StaffAccountsButton.Visibility = user.CanConfigureProgram
            ? Visibility.Visible
            : Visibility.Collapsed;

        ShowView("pos");
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        // Evenimentul se declanșează și în timpul construirii ferestrei, înainte
        // ca zona de conținut să existe.
        if (!IsLoaded && ViewHost is null)
            return;

        if (sender is RadioButton { Tag: string key })
            ShowView(key);
    }

    private void ShowView(string key)
    {
        if (!_views.TryGetValue(key, out var view))
        {
            view = key switch
            {
                "pos" => new PosView(),
                "customers" => new CustomersView(),
                "reports" => new ReportsView(),
                "admin" => new AdminView(),
                _ => new PosView(),
            };

            _views[key] = view;
        }

        ViewHost.Content = view;

        // Ecranele se reîmprospătează la fiecare intrare: datele pot fi schimbate
        // de pe altă casă sau de o operațiune făcută între timp pe alt ecran.
        if (view is IRefreshable refreshable)
            refreshable.Refresh();
    }

    /// <summary>Duce utilizatorul direct la lista de conturi, din orice ecran.</summary>
    public void ShowUserAccounts()
    {
        NavAdmin.IsChecked = true;
        ShowView("admin");

        if (_views.TryGetValue("admin", out var view) && view is AdminView admin)
            admin.ShowUsersTab();
    }

    private void OnStaffAccounts(object sender, RoutedEventArgs e) => ShowUserAccounts();

    private void OnChangePassword(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordWindow { Owner = this };
        dialog.ShowDialog();
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm("Ieși din cont? Programul se va închide."))
            return;

        _signingOut = true;
        AppServices.Current.SignOut();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_signingOut && !Dialogs.Confirm("Închizi programul?"))
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}

/// <summary>Ecran care își reîncarcă datele când devine vizibil.</summary>
public interface IRefreshable
{
    void Refresh();
}
