using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;
using Microsoft.Win32;

namespace Fidelizare.App.Views;

/// <summary>
/// Panoul de administrare: regulile programului, nivelurile, catalogul de
/// recompense, conturile personalului și operațiunile de întreținere.
/// </summary>
public partial class AdminView : UserControl, IRefreshable
{
    public AdminView()
    {
        InitializeComponent();

        var user = AppServices.Current.RequireUser;

        // Responsabilul vede rapoartele și catalogul, dar conturile și setările de
        // sistem rămân la administrator.
        if (!user.CanConfigureProgram)
        {
            UsersTab.Visibility = Visibility.Collapsed;
            SystemTab.Visibility = Visibility.Collapsed;
        }

        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        try
        {
            LoadRules();
            LoadTiers();
            LoadRewards();
            LoadCampaigns();
            LoadFields();
            LoadBranding();
            LoadUsers();
            LoadSystem();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Datele de administrare nu au putut fi încărcate: {ex.Message}");
        }
    }

    // ================================================================ Reguli

    private void LoadRules()
    {
        var rules = AppServices.Current.Loyalty.GetRules();

        LeiPerPointBox.Text = AmountParser.Format(rules.LeiPerPoint);
        MinPurchaseBox.Text = AmountParser.Format(rules.MinPurchaseForPoints);
        PointValueBox.Text = rules.PointValueLei.ToString("0.00##");
        MinRedeemBox.Text = rules.MinimumRedeemablePoints.ToString();
        SelfVoidBox.Text = rules.SelfVoidMinutes.ToString();
        ValidityBox.Text = rules.PointsValidityMonths.ToString();
        WarningDaysBox.Text = rules.ExpiryWarningDays.ToString();
        WelcomeBox.Text = rules.WelcomeBonusPoints.ToString();
        BirthdayBox.Text = rules.BirthdayBonusPoints.ToString();
        BirthdayWindowBox.Text = rules.BirthdayWindowDays.ToString();
        QualifyingBox.Text = rules.QualifyingPeriodMonths.ToString();
        TierBasisBox.SelectedIndex = (int)rules.TierBasis - 1;
        UpdateTierBasisHint();
        StoreNameBox.Text = rules.StoreName;
        ProgramNameBox.Text = rules.ProgramName;

        RulesStatus.Text = string.Empty;
    }

    private void OnSaveRules(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;
        var rules = services.Loyalty.GetRules();

        if (!TryReadAmount(LeiPerPointBox, "lei pentru un punct", out var leiPerPoint) ||
            !TryReadAmount(MinPurchaseBox, "valoarea minimă a bonului", out var minPurchase) ||
            !TryReadAmount(PointValueBox, "valoarea unui punct", out var pointValue) ||
            !TryReadInt(MinRedeemBox, "pragul minim de revendicare", out var minRedeem) ||
            !TryReadInt(SelfVoidBox, "intervalul de anulare de către casier", out var selfVoid) ||
            !TryReadInt(ValidityBox, "valabilitatea punctelor", out var validity) ||
            !TryReadInt(WarningDaysBox, "avertizarea de expirare", out var warningDays) ||
            !TryReadInt(WelcomeBox, "bonusul de înscriere", out var welcome) ||
            !TryReadInt(BirthdayBox, "bonusul de zi de naștere", out var birthday) ||
            !TryReadInt(BirthdayWindowBox, "fereastra aniversară", out var birthdayWindow) ||
            !TryReadInt(QualifyingBox, "perioada de calificare", out var qualifying))
        {
            return;
        }

        rules.LeiPerPoint = leiPerPoint;
        rules.MinPurchaseForPoints = minPurchase;
        rules.PointValueLei = pointValue;
        rules.MinimumRedeemablePoints = minRedeem;
        rules.SelfVoidMinutes = selfVoid;
        rules.PointsValidityMonths = validity;
        rules.ExpiryWarningDays = warningDays;
        rules.WelcomeBonusPoints = welcome;
        rules.BirthdayBonusPoints = birthday;
        rules.BirthdayWindowDays = birthdayWindow;
        rules.QualifyingPeriodMonths = qualifying;
        rules.TierBasis = (TierBasis)(TierBasisBox.SelectedIndex + 1);
        rules.StoreName = StoreNameBox.Text.Trim();
        rules.ProgramName = ProgramNameBox.Text.Trim();

        var result = services.Loyalty.SaveRules(rules, services.RequireUser);

        if (result.Failed)
        {
            Dialogs.Error(result.Error!);
            return;
        }

        RulesStatus.Text = $"Salvat la {services.Clock.Now:HH:mm}.";
        Dialogs.Info("Regulile au fost salvate. Se aplică începând cu următoarea operațiune.");
    }

    private void OnReloadRules(object sender, RoutedEventArgs e) => LoadRules();

    private static bool TryReadAmount(TextBox box, string fieldName, out decimal value)
    {
        if (AmountParser.TryParse(box.Text, out value))
            return true;

        Dialogs.Error($"Valoarea introdusă pentru „{fieldName}” nu este un număr valid.");
        box.Focus();
        return false;
    }

    private static bool TryReadInt(TextBox box, string fieldName, out int value)
    {
        if (int.TryParse(box.Text.Trim(), out value))
            return true;

        Dialogs.Error($"Valoarea introdusă pentru „{fieldName}” trebuie să fie un număr întreg.");
        box.Focus();
        return false;
    }

    // ============================================================== Niveluri

    private void LoadTiers()
    {
        using var uow = AppServices.Current.Factory.Create();
        TiersGrid.ItemsSource = uow.Tiers.GetAll();
    }

    private void OnNewTier(object sender, RoutedEventArgs e)
    {
        var dialog = new TierWindow(null) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadTiers();
    }

    private void OnEditTier(object sender, RoutedEventArgs e)
    {
        if (TiersGrid.SelectedItem is not Tier tier)
        {
            Dialogs.Info("Selectează un nivel din listă.");
            return;
        }

        var dialog = new TierWindow(tier) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadTiers();
    }

    private void OnDeleteTier(object sender, RoutedEventArgs e)
    {
        if (TiersGrid.SelectedItem is not Tier tier)
        {
            Dialogs.Info("Selectează un nivel din listă.");
            return;
        }

        var services = AppServices.Current;

        using (var uow = services.Factory.Create())
        {
            // Programul are nevoie de cel puțin un nivel: fără el, nu s-ar putea
            // stabili multiplicatorul niciunei cumpărări.
            if (uow.Tiers.GetAll().Count <= 1)
            {
                Dialogs.Warn("Trebuie să rămână cel puțin un nivel definit.");
                return;
            }
        }

        if (!Dialogs.ConfirmDestructive(
                $"Ștergi nivelul „{tier.Name}”?\n\n" +
                "Clienții aflați pe acest nivel vor fi recalculați pe nivelul imediat inferior. " +
                "Recompensele rezervate acestui nivel devin disponibile tuturor."))
        {
            return;
        }

        try
        {
            using var uow = services.Factory.Create();
            uow.Tiers.Delete(tier.Id);

            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = services.Clock.Now,
                UserId = services.RequireUser.Id,
                UserName = services.RequireUser.Username,
                Action = "TierDeleted",
                EntityType = "Tier",
                EntityId = tier.Id,
                Details = tier.Name,
            });

            uow.Commit();
            LoadTiers();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Nivelul nu a putut fi șters: {ex.Message}");
        }
    }

    // ============================================================ Recompense

    private void LoadRewards()
    {
        using var uow = AppServices.Current.Factory.Create();
        RewardsGrid.ItemsSource = uow.Rewards.GetAll();
    }

    private void OnNewReward(object sender, RoutedEventArgs e)
    {
        var dialog = new RewardWindow(null) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadRewards();
    }

    private void OnEditReward(object sender, RoutedEventArgs e)
    {
        if (RewardsGrid.SelectedItem is not Reward reward)
        {
            Dialogs.Info("Selectează o recompensă din listă.");
            return;
        }

        var dialog = new RewardWindow(reward) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadRewards();
    }

    private void OnDeleteReward(object sender, RoutedEventArgs e)
    {
        if (RewardsGrid.SelectedItem is not Reward reward)
        {
            Dialogs.Info("Selectează o recompensă din listă.");
            return;
        }

        if (!Dialogs.ConfirmDestructive(
                $"Ștergi recompensa „{reward.Name}” din catalog?\n\n" +
                "Recompensele deja acordate clienților rămân în istoricul lor. " +
                "Dacă vrei doar să o retragi temporar, folosește „Modifică” și debifează „Activă”."))
        {
            return;
        }

        var services = AppServices.Current;

        try
        {
            using var uow = services.Factory.Create();
            uow.Rewards.Delete(reward.Id);

            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = services.Clock.Now,
                UserId = services.RequireUser.Id,
                UserName = services.RequireUser.Username,
                Action = "RewardDeleted",
                EntityType = "Reward",
                EntityId = reward.Id,
                Details = reward.Name,
            });

            uow.Commit();
            LoadRewards();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Recompensa nu a putut fi ștearsă: {ex.Message}");
        }
    }

    // =========================================================== Utilizatori

    private void LoadUsers()
    {
        if (UsersTab.Visibility != Visibility.Visible)
            return;

        UsersGrid.ItemsSource = AppServices.Current.Auth.GetUsers();
    }

    private void OnNewUser(object sender, RoutedEventArgs e)
    {
        var dialog = new UserWindow(null) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadUsers();
    }

    private void OnEditUser(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not AppUser user)
        {
            Dialogs.Info("Selectează un cont din listă.");
            return;
        }

        var dialog = new UserWindow(user) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
        {
            LoadUsers();
            AppServices.Current.RefreshCurrentUser();
        }
    }

    private void OnResetPassword(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not AppUser user)
        {
            Dialogs.Info("Selectează un cont din listă.");
            return;
        }

        var dialog = new ResetPasswordWindow(user) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadUsers();
    }

    // ================================================================ Sistem

    private void LoadSystem()
    {
        if (SystemTab.Visibility != Visibility.Visible)
            return;

        var services = AppServices.Current;

        DatabasePathBox.Text = services.DatabasePath;

        var version = typeof(AdminView).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        AboutText.Text =
            $"Program de fidelizare, versiunea {version}.\n" +
            "Aplicație autonomă: nu are nevoie de internet, de server sau de instalare.\n" +
            "Toate datele sunt păstrate local, în fișierul indicat mai sus.";
    }

    private void OnBackup(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;

        var dialog = new SaveFileDialog
        {
            Title = "Salvează copia de siguranță",
            FileName = $"fidelizare-{services.Clock.Now:yyyy-MM-dd-HHmm}.db",
            Filter = "Bază de date (*.db)|*.db|Toate fișierele (*.*)|*.*",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            // Copia se face după o închidere a conexiunilor din pool, ca fișierul
            // să nu fie surprins la mijlocul unei scrieri.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Copy(services.DatabasePath, dialog.FileName, overwrite: true);

            Dialogs.Info($"Copia de siguranță a fost salvată:\n{dialog.FileName}");
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Copia nu a putut fi salvată: {ex.Message}");
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(AppServices.Current.DatabasePath);
            if (string.IsNullOrEmpty(folder))
                return;

            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Folderul nu a putut fi deschis: {ex.Message}");
        }
    }

    private void OnRunExpiry(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;

        try
        {
            var expired = services.Loyalty.RunExpiry(services.RequireUser);

            Dialogs.Info(expired == 0
                ? "Niciun punct nu era ajuns la termen."
                : $"{expired} puncte au fost scoase din solduri, fiind expirate.");
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Verificarea nu a putut fi făcută: {ex.Message}");
        }
    }

    private void OnGenerateDemo(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.ConfirmDestructive(
                "Generezi 60 de clienți cu istoric inventat?\n\n" +
                "Operațiunea funcționează doar dacă baza de date nu are încă niciun client. " +
                "Datele generate nu se pot șterge automat după aceea — ar trebui pornit de la o bază nouă."))
        {
            return;
        }

        var services = AppServices.Current;

        try
        {
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var result = services.Demo.Generate(services.RequireUser);

            if (result.Failed)
            {
                Dialogs.Warn(result.Error!);
                return;
            }

            Dialogs.Info($"{result.Value} clienți demonstrativi au fost creați, cu istoric de cumpărături.");
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Datele demonstrative nu au putut fi generate: {ex.Message}");
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
        }
    }
}
