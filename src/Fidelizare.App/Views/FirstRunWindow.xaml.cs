using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.App.Views;

/// <summary>
/// Configurarea de la prima pornire, în doi pași.
///
/// Pasul întâi creează administratorul. Pasul al doilea creează conturile cu care
/// se lucrează efectiv la casă, și există tocmai pentru că altfel nu s-ar face
/// niciodată: proprietarul ar rămâne logat ca administrator și ar lucra cu el
/// toată ziua. Jurnalul de audit ar scrie atunci „Administrator” la fiecare
/// vânzare și la fiecare corecție de puncte, adică nu ar mai răspunde la
/// întrebarea pentru care a fost făcut.
///
/// Programul nu este livrat cu niciun cont implicit: unul de tip „admin/admin”
/// ar ajunge identic la toate magazinele care primesc executabilul.
/// </summary>
public partial class FirstRunWindow : Window
{
    private readonly ObservableCollection<string> _createdStaff = [];

    private AppUser? _administrator;

    public FirstRunWindow()
    {
        InitializeComponent();
        this.FitToWorkArea();

        StaffList.ItemsSource = _createdStaff;

        ShowStep1();
        UpdateRoleHint();

        Loaded += (_, _) => NameBox.Focus();
    }

    // ================================================================= Pași

    private void ShowStep1()
    {
        StepText.Text = "PASUL 1 DIN 2";
        TitleText.Text = "Bine ai venit";
        SubtitleText.Text =
            "Programul pornește pentru prima dată pe acest calculator. " +
            "Începem cu datele magazinului și cu contul de administrator.";

        Step1Panel.Visibility = Visibility.Visible;
        Step2Panel.Visibility = Visibility.Collapsed;

        CancelButton.Content = "Renunță";
        NextButton.Content = "Continuă";
    }

    private void ShowStep2()
    {
        StepText.Text = "PASUL 2 DIN 2";
        TitleText.Text = "Conturile personalului";
        SubtitleText.Text =
            "Creează acum conturile cu care se va lucra la casă. " +
            "Contul de administrator rămâne pentru configurare, nu pentru vânzări zilnice.";

        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;

        // Administratorul este deja salvat, deci de aici nu se mai poate renunța
        // la configurare — se poate doar amâna crearea conturilor de lucru.
        CancelButton.Content = "Adaug mai târziu";
        NextButton.Content = "Gata, intră în program";

        StaffNameBox.Focus();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_administrator is null)
        {
            if (CreateAdministrator())
                ShowStep2();

            return;
        }

        WarnIfNoStaffAccounts();
        DialogResult = true;
    }

    // ==================================================== Pasul 1: admin

    private bool CreateAdministrator()
    {
        HideError();

        var name = NameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(StoreNameBox.Text))
        {
            ShowError("Completează numele magazinului.");
            StoreNameBox.Focus();
            return false;
        }

        if (name.Length < 3)
        {
            ShowError("Numele trebuie să aibă cel puțin 3 caractere.");
            NameBox.Focus();
            return false;
        }

        if (password != ConfirmBox.Password)
        {
            ShowError("Cele două parole nu coincid.");
            ConfirmBox.Clear();
            ConfirmBox.Focus();
            return false;
        }

        var strength = PasswordHasher.ValidateStrength(password);
        if (strength is not null)
        {
            ShowError(strength);
            PasswordBox.Focus();
            return false;
        }

        var services = AppServices.Current;

        // Primul administrator se creează direct: nu există încă cineva care să
        // aibă dreptul de a crea conturi, deci verificarea de rol nu se poate aplica.
        var admin = new AppUser
        {
            Username = name,
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = services.Clock.Now,
        };

        var (hash, salt, iterations) = PasswordHasher.Create(password);
        admin.PasswordHash = hash;
        admin.PasswordSalt = salt;
        admin.PasswordIterations = iterations;

        try
        {
            using var uow = services.Factory.Create();

            if (uow.Users.UsernameExists(name))
            {
                ShowError($"Există deja un cont cu numele „{name}”.");
                return false;
            }

            admin.Id = uow.Users.Insert(admin);

            var rules = LoyaltyRules.Load(uow.Settings);
            rules.StoreName = StoreNameBox.Text.Trim();
            rules.ProgramName = string.IsNullOrWhiteSpace(ProgramNameBox.Text)
                ? rules.ProgramName
                : ProgramNameBox.Text.Trim();
            rules.Save(uow.Settings);

            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = services.Clock.Now,
                UserId = admin.Id,
                UserName = admin.Username,
                Action = "InitialSetup",
                EntityType = "User",
                EntityId = admin.Id,
                Details = $"Configurare inițială pentru {rules.StoreName}.",
            });

            uow.Commit();
        }
        catch (Exception ex)
        {
            ShowError($"Contul nu a putut fi creat: {ex.Message}");
            return false;
        }

        _administrator = admin;
        return true;
    }

    // ============================================ Pasul 2: conturi de lucru

    private void OnStaffRoleChanged(object sender, SelectionChangedEventArgs e) => UpdateRoleHint();

    private void UpdateRoleHint()
    {
        if (RoleHintText is null)
            return;

        RoleHintText.Text = StaffRoleBox.SelectedIndex == 1
            ? "Responsabilul face tot ce face casierul, plus corecții de puncte, anulări și rapoarte."
            : "Casierul caută clienți, îi înscrie, înregistrează vânzări și acordă recompense.";
    }

    private void OnAddStaff(object sender, RoutedEventArgs e)
    {
        HideError();

        if (_administrator is null)
            return;

        var name = StaffNameBox.Text.Trim();

        if (name.Length < 3)
        {
            ShowError("Numele angajatului trebuie să aibă cel puțin 3 caractere.");
            StaffNameBox.Focus();
            return;
        }

        if (StaffPasswordBox.Password != StaffConfirmBox.Password)
        {
            ShowError("Cele două parole nu coincid.");
            StaffConfirmBox.Clear();
            StaffConfirmBox.Focus();
            return;
        }

        var user = new AppUser
        {
            Username = name,
            Role = StaffRoleBox.SelectedIndex == 1 ? UserRole.Manager : UserRole.Cashier,
            IsActive = true,
        };

        // Contul se creează prin serviciul obișnuit, cu administratorul tocmai
        // făcut ca autor: aceleași verificări și aceeași urmă în jurnal ca mai târziu.
        var result = AppServices.Current.Auth.CreateUser(user, StaffPasswordBox.Password, _administrator);

        if (result.Failed)
        {
            ShowError(result.Error!);
            return;
        }

        _createdStaff.Add($"{result.Value!.Username}  ·  {result.Value.RoleLabel}");
        StaffListCard.Visibility = Visibility.Visible;

        StaffNameBox.Clear();
        StaffPasswordBox.Clear();
        StaffConfirmBox.Clear();
        StaffNameBox.Focus();
    }

    /// <summary>Ultima ocazie de a atrage atenția, fără a bloca pe nimeni.</summary>
    private void WarnIfNoStaffAccounts()
    {
        if (_createdStaff.Count > 0)
            return;

        Dialogs.Warn(
            "Nu ai creat niciun cont de lucru.\n\n" +
            "Vei intra în program cu contul de administrator. Poți crea conturile " +
            "pentru casieri oricând, din Administrare → Utilizatori — până atunci, " +
            "toate operațiunile vor apărea în jurnal ca fiind făcute de administrator.");
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        // La pasul al doilea administratorul există deja, deci configurarea a
        // reușit: renunțarea înseamnă doar amânarea conturilor de lucru.
        if (_administrator is not null)
        {
            WarnIfNoStaffAccounts();
            DialogResult = true;
            return;
        }

        DialogResult = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Închiderea din colț, după crearea administratorului, nu trebuie să pară
        // un eșec: baza de date are deja tot ce îi trebuie ca să pornească.
        if (_administrator is not null && DialogResult is null)
            DialogResult = true;

        base.OnClosing(e);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorBanner.Visibility = Visibility.Collapsed;
}
