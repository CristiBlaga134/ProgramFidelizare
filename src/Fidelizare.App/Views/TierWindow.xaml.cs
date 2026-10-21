using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;

namespace Fidelizare.App.Views;

/// <summary>Adăugarea sau modificarea unui nivel de fidelitate.</summary>
public partial class TierWindow : Window
{
    private readonly Tier? _existing;

    public TierWindow(Tier? existing)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _existing = existing;

        if (existing is null)
        {
            TitleText.Text = "Nivel nou";
            MultiplierBox.Text = "1";
            MinSpendBox.Text = "0,00";
            StandingBox.Text = "0";
            ColorBox.Text = "#6B7280";
        }
        else
        {
            TitleText.Text = $"Nivelul {existing.Name}";
            NameBox.Text = existing.Name;
            MinSpendBox.Text = AmountParser.Format(existing.Threshold);
            MultiplierBox.Text = existing.EarnMultiplier.ToString("0.##");
            StandingBox.Text = existing.StandingDiscountPercent.ToString("0.##");
            ColorBox.Text = existing.ColorHex;
            BenefitsBox.Text = existing.Benefits ?? string.Empty;
        }

        UpdateColorPreview();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnColorChanged(object sender, TextChangedEventArgs e) => UpdateColorPreview();

    private void UpdateColorPreview()
    {
        if (ColorPreview is null)
            return;

        try
        {
            ColorPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorBox.Text));
        }
        catch (Exception)
        {
            // Culoarea este incompletă în timpul tastării; previzualizarea rămâne goală.
            ColorPreview.Background = Brushes.Transparent;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        var name = NameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Denumirea nivelului este obligatorie.");
            return;
        }

        if (!AmountParser.TryParse(MinSpendBox.Text, out var minSpend) || minSpend < 0)
        {
            ShowError("Pragul de rulaj trebuie să fie un număr pozitiv.");
            return;
        }

        if (!AmountParser.TryParse(MultiplierBox.Text, out var multiplier) || multiplier <= 0)
        {
            ShowError("Multiplicatorul trebuie să fie mai mare decât zero.");
            return;
        }

        if (multiplier > 10m)
        {
            ShowError("Un multiplicator peste 10 este aproape sigur o greșeală de tastare.");
            return;
        }

        if (!AmountParser.TryParse(StandingBox.Text, out var standing) || standing is < 0 or > 100)
        {
            ShowError("Reducerea permanentă trebuie să fie între 0 și 100.");
            return;
        }

        var services = AppServices.Current;

        var tier = new Tier
        {
            Id = _existing?.Id ?? 0,
            Name = name,
            Threshold = minSpend,
            EarnMultiplier = multiplier,
            StandingDiscountPercent = standing,
            ColorHex = string.IsNullOrWhiteSpace(ColorBox.Text) ? "#6B7280" : ColorBox.Text.Trim(),
            SortOrder = _existing?.SortOrder ?? 0,
            Benefits = string.IsNullOrWhiteSpace(BenefitsBox.Text) ? null : BenefitsBox.Text.Trim(),
        };

        try
        {
            using var uow = services.Factory.Create();

            var all = uow.Tiers.GetAll();

            if (all.Any(t => t.Id != tier.Id && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                ShowError($"Există deja un nivel cu numele „{name}”.");
                return;
            }

            if (all.Any(t => t.Id != tier.Id && t.Threshold == minSpend))
            {
                ShowError("Există deja un nivel cu același prag de rulaj. Pragurile trebuie să fie distincte.");
                return;
            }

            // Cel mai de jos nivel trebuie să pornească de la zero, altfel un client
            // fără cumpărături nu s-ar încadra nicăieri.
            var isLowest = !all.Any(t => t.Id != tier.Id && t.Threshold < minSpend);
            if (isLowest && minSpend != 0m)
            {
                ShowError("Nivelul cu cel mai mic prag trebuie să pornească de la 0 lei.");
                return;
            }

            if (_existing is null)
            {
                tier.SortOrder = all.Count + 1;
                tier.Id = uow.Tiers.Insert(tier);
            }
            else
            {
                uow.Tiers.Update(tier);
            }

            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = services.Clock.Now,
                UserId = services.RequireUser.Id,
                UserName = services.RequireUser.Username,
                Action = _existing is null ? "TierCreated" : "TierUpdated",
                EntityType = "Tier",
                EntityId = tier.Id,
                Details = $"{tier.Name}: prag {tier.Threshold:N2} lei, multiplicator {tier.EarnMultiplier:0.##}",
            });

            uow.Commit();
        }
        catch (Exception ex)
        {
            ShowError($"Nivelul nu a putut fi salvat: {ex.Message}");
            return;
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
