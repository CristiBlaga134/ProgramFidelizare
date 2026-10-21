using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;
using Microsoft.Win32;

namespace Fidelizare.App.Views;

/// <summary>
/// Partea din panoul de administrare care ține de configurarea programului de
/// către magazin: campanii, câmpuri proprii, identitate vizuală și schimbul de
/// date cu exteriorul.
/// </summary>
public partial class AdminView
{
    /// <summary>
    /// Deschide direct fila conturilor. Folosită de scurtăturile din alte ecrane:
    /// crearea conturilor de personal este operațiunea cel mai ușor de uitat, iar
    /// drumul până la ea trecea prin două niveluri de meniu.
    /// </summary>
    public void ShowUsersTab()
    {
        if (UsersTab.Visibility == Visibility.Visible)
            UsersTab.IsSelected = true;
    }

    // ============================================================== Campanii

    private void LoadCampaigns()
    {
        using var uow = AppServices.Current.Factory.Create();
        CampaignsGrid.ItemsSource = uow.Campaigns.GetAll();
    }

    private void OnNewCampaign(object sender, RoutedEventArgs e)
    {
        var dialog = new CampaignWindow(null) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadCampaigns();
    }

    private void OnEditCampaign(object sender, RoutedEventArgs e)
    {
        if (CampaignsGrid.SelectedItem is not Campaign campaign)
        {
            Dialogs.Info("Selectează o campanie din listă.");
            return;
        }

        var dialog = new CampaignWindow(campaign) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadCampaigns();
    }

    private void OnDeleteCampaign(object sender, RoutedEventArgs e)
    {
        if (CampaignsGrid.SelectedItem is not Campaign campaign)
        {
            Dialogs.Info("Selectează o campanie din listă.");
            return;
        }

        if (!Dialogs.ConfirmDestructive(
                $"Ștergi campania „{campaign.Name}”?\n\n" +
                "Punctele deja acordate în timpul ei rămân neschimbate, iar bonurile păstrează " +
                "numele campaniei. Dacă vrei doar să o oprești, modific-o și debifează „Campanie activă”."))
        {
            return;
        }

        var services = AppServices.Current;

        try
        {
            using var uow = services.Factory.Create();
            uow.Campaigns.Delete(campaign.Id);

            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = services.Clock.Now,
                UserId = services.RequireUser.Id,
                UserName = services.RequireUser.Username,
                Action = "CampaignDeleted",
                EntityType = "Campaign",
                EntityId = campaign.Id,
                Details = $"{campaign.Name} ({campaign.PeriodLabel})",
            });

            uow.Commit();
            LoadCampaigns();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Campania nu a putut fi ștearsă: {ex.Message}");
        }
    }

    // ======================================================= Câmpuri proprii

    private void LoadFields()
    {
        using var uow = AppServices.Current.Factory.Create();
        FieldsGrid.ItemsSource = uow.CustomFields.GetDefinitions(includeInactive: true);
    }

    private void OnNewField(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomFieldWindow(null) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadFields();
    }

    private void OnEditField(object sender, RoutedEventArgs e)
    {
        if (FieldsGrid.SelectedItem is not CustomFieldDefinition field)
        {
            Dialogs.Info("Selectează un câmp din listă.");
            return;
        }

        var dialog = new CustomFieldWindow(field) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            LoadFields();
    }

    private void OnDeleteField(object sender, RoutedEventArgs e)
    {
        if (FieldsGrid.SelectedItem is not CustomFieldDefinition field)
        {
            Dialogs.Info("Selectează un câmp din listă.");
            return;
        }

        if (!Dialogs.ConfirmDestructive(
                $"Ștergi câmpul „{field.Label}”?\n\n" +
                "Valorile completate pentru toți clienții se pierd definitiv. " +
                "Dacă vrei doar să nu mai apară în formular, modific-l și debifează „Câmp activ”."))
        {
            return;
        }

        var services = AppServices.Current;

        try
        {
            using var uow = services.Factory.Create();
            uow.CustomFields.DeleteDefinition(field.Id);

            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = services.Clock.Now,
                UserId = services.RequireUser.Id,
                UserName = services.RequireUser.Username,
                Action = "CustomFieldDeleted",
                EntityType = "CustomField",
                EntityId = field.Id,
                Details = $"{field.Label} ({field.Key})",
            });

            uow.Commit();
            LoadFields();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Câmpul nu a putut fi șters: {ex.Message}");
        }
    }

    // ============================================================ Identitate

    /// <summary>Câteva culori pregătite, ca magazinul să nu fie nevoit să caute coduri hexazecimale.</summary>
    private static readonly (string Name, string Hex)[] Palette =
    [
        ("Albastru", "#3B5BDB"),
        ("Indigo", "#5F3DC4"),
        ("Prună", "#862E9C"),
        ("Zmeură", "#C2255C"),
        ("Cărămiziu", "#C92A2A"),
        ("Chihlimbar", "#B36A00"),
        ("Verde", "#2B8A3E"),
        ("Petrol", "#0B7285"),
        ("Grafit", "#343A40"),
    ];

    private void LoadBranding()
    {
        if (BrandingTab.Visibility != Visibility.Visible)
            return;

        var rules = AppServices.Current.Loyalty.GetRules();

        AccentBox.Text = rules.AccentColorHex;
        CardPrefixBox.Text = rules.CardCodePrefix;
        CardDigitsBox.Text = rules.CardCodeDigits.ToString();

        BuildPalette();
        UpdateAccentPreview();
        UpdateCardSample();
        RefreshLogoPreview();

        BrandingStatus.Text = string.Empty;
    }

    private void BuildPalette()
    {
        if (PalettePanel.Children.Count > 0)
            return;

        foreach (var (name, hex) in Palette)
        {
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = name,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderBrush = (Brush)FindResource("Border"),
                BorderThickness = new Thickness(1),
            };

            swatch.MouseLeftButtonUp += (_, _) => AccentBox.Text = hex;
            PalettePanel.Children.Add(swatch);
        }
    }

    private void OnAccentChanged(object sender, TextChangedEventArgs e) => UpdateAccentPreview();

    private void UpdateAccentPreview()
    {
        if (AccentPreview is null)
            return;

        if (!LoyaltyRules.IsValidHexColor(AccentBox.Text))
        {
            AccentPreview.Background = Brushes.Transparent;
            return;
        }

        AccentPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AccentBox.Text));
    }

    private void OnCardFormatChanged(object sender, TextChangedEventArgs e) => UpdateCardSample();

    private void UpdateCardSample()
    {
        if (CardSampleText is null)
            return;

        var prefix = CardPrefixBox.Text.Trim();

        CardSampleText.Text = int.TryParse(CardDigitsBox.Text.Trim(), out var digits) && digits is >= 3 and <= 12
            ? prefix + new string('0', digits - 1) + "1"
            : "—";
    }

    private void RefreshLogoPreview()
    {
        var logo = Branding.LoadLogo();

        LogoPreview.Source = logo;
        LogoPreview.Visibility = logo is null ? Visibility.Collapsed : Visibility.Visible;
        NoLogoText.Visibility = logo is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPickLogo(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Alege imaginea pentru logo",
            Filter = "Imagini (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Toate fișierele (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
            return;

        var result = Branding.SaveLogo(dialog.FileName);

        if (result.Failed)
        {
            Dialogs.Error(result.Error!);
            return;
        }

        RefreshLogoPreview();
        Dialogs.Info("Logo-ul a fost salvat. Apare în bara laterală la următoarea pornire a programului.");
    }

    private void OnRemoveLogo(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm("Scoți logo-ul din program?"))
            return;

        var result = Branding.SaveLogo(null);

        if (result.Failed)
        {
            Dialogs.Error(result.Error!);
            return;
        }

        RefreshLogoPreview();
    }

    private void OnSaveBranding(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;
        var rules = services.Loyalty.GetRules();

        if (!int.TryParse(CardDigitsBox.Text.Trim(), out var digits))
        {
            Dialogs.Error("Numărul de cifre trebuie să fie un număr întreg.");
            return;
        }

        rules.AccentColorHex = AccentBox.Text.Trim();
        rules.CardCodePrefix = CardPrefixBox.Text.Trim();
        rules.CardCodeDigits = digits;

        var result = services.Loyalty.SaveRules(rules, services.RequireUser);

        if (result.Failed)
        {
            Dialogs.Error(result.Error!);
            return;
        }

        // Culoarea se aplică pe loc: pensulele din temă sunt aceleași obiecte pe
        // care le folosesc toate ecranele deschise.
        Branding.ApplyAccent(rules.AccentColorHex);

        BrandingStatus.Text = $"Salvat la {services.Clock.Now:HH:mm}.";
    }

    private void UpdateTierBasisHint()
    {
        if (TierBasisHint is null)
            return;

        TierBasisHint.Text = TierBasisBox.SelectedIndex switch
        {
            1 => "Pragurile din fila Niveluri se citesc ca puncte acumulate.",
            2 => "Pragurile din fila Niveluri se citesc ca număr de cumpărături.",
            _ => "Pragurile din fila Niveluri se citesc ca lei cheltuiți.",
        };
    }

    private void OnTierBasisChanged(object sender, SelectionChangedEventArgs e) => UpdateTierBasisHint();

    // ======================================================= Import și export

    private void OnDownloadTemplate(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvează fișierul model",
            FileName = "model-import-clienti.csv",
            Filter = "Fișier CSV (*.csv)|*.csv",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        var result = AppServices.Current.Transfer.WriteImportTemplate(dialog.FileName);

        if (result.Failed)
        {
            Dialogs.Error(result.Error!);
            return;
        }

        Dialogs.Info(
            "Fișierul model a fost salvat. Completează-l în Excel, păstrând prima linie neschimbată, " +
            "apoi folosește „Importă clienți din CSV”.");
    }

    private void OnImportCustomers(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Alege fișierul cu clienți",
            Filter = "Fișiere CSV (*.csv)|*.csv|Fișiere text (*.txt)|*.txt|Toate fișierele (*.*)|*.*",
        };

        if (picker.ShowDialog() != true)
            return;

        var services = AppServices.Current;
        var preview = services.Transfer.PreviewCustomers(picker.FileName);

        if (preview.Failed)
        {
            Dialogs.Error(preview.Error!);
            return;
        }

        var report = preview.Value!;

        // Importul se arată înainte de a se executa: pe o listă de câteva sute de
        // clienți, o coloană greșit denumită se observă mult mai greu după.
        var message =
            $"Fișierul conține {report.TotalRows} rânduri.\n\n" +
            $"Se vor crea {report.WillCreate} clienți.\n" +
            $"Se vor ignora {report.WillSkip}.\n\n" +
            $"Coloane recunoscute: {(report.RecognizedColumns.Count == 0 ? "niciuna" : string.Join(", ", report.RecognizedColumns))}\n";

        if (report.IgnoredColumns.Count > 0)
            message += $"Coloane ignorate: {string.Join(", ", report.IgnoredColumns)}\n";

        if (report.Issues.Count > 0)
        {
            message += "\nMotivele ignorării:\n" +
                       string.Join("\n", report.Issues.Take(12).Select(i => "  · " + i));

            if (report.Issues.Count > 12)
                message += $"\n  … și încă {report.Issues.Count - 12}.";
        }

        if (!report.CanImport)
        {
            Dialogs.Warn(message + "\n\nNiciun client nu poate fi creat din acest fișier.");
            return;
        }

        if (!Dialogs.ConfirmDestructive(message + $"\n\nContinui importul celor {report.WillCreate} clienți?"))
            return;

        try
        {
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            var result = services.Transfer.ImportCustomers(picker.FileName, services.RequireUser);

            if (result.Failed)
            {
                Dialogs.Error(result.Error!);
                return;
            }

            var outcome = result.Value!;

            Dialogs.Info(
                $"Import încheiat.\n\n" +
                $"Clienți creați: {outcome.Created}\n" +
                $"Rânduri ignorate: {outcome.Skipped}\n" +
                $"Rânduri eșuate: {outcome.Failed}\n" +
                (outcome.PointsGranted > 0 ? $"Puncte preluate: {outcome.PointsGranted}\n" : string.Empty));
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
        }
    }

    private void OnExportCustomers(object sender, RoutedEventArgs e) =>
        RunExport("clienti", path => AppServices.Current.Transfer.ExportCustomers(path));

    private void OnExportPurchases(object sender, RoutedEventArgs e) =>
        RunExport("cumparaturi", path =>
        {
            var now = AppServices.Current.Clock.Now;
            return AppServices.Current.Transfer.ExportPurchases(path, now.AddMonths(-12), now.AddDays(1));
        });

    private void OnExportLedger(object sender, RoutedEventArgs e) =>
        RunExport("istoric-puncte", path =>
        {
            var now = AppServices.Current.Clock.Now;
            return AppServices.Current.Transfer.ExportPointLedger(path, now.AddMonths(-12), now.AddDays(1));
        });

    /// <summary>Alegerea fișierului, exportul și raportarea, la fel pentru toate tipurile de date.</summary>
    private void RunExport(string baseName, Func<string, OperationResult<int>> export)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvează exportul",
            FileName = $"{baseName}-{AppServices.Current.Clock.Now:yyyy-MM-dd}.csv",
            Filter = "Fișier CSV (*.csv)|*.csv",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            var result = export(dialog.FileName);

            if (result.Failed)
            {
                Dialogs.Error(result.Error!);
                return;
            }

            var open = Dialogs.Confirm(
                $"{result.Value} rânduri au fost scrise în:\n{dialog.FileName}\n\nDeschizi folderul?");

            if (open)
            {
                var folder = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folder))
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
                }
            }
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
        }
    }
}
