using System.Windows;
using System.Windows.Controls;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;

namespace Fidelizare.App.Views;

/// <summary>Adăugarea sau modificarea unei recompense din catalog.</summary>
public partial class RewardWindow : Window
{
    /// <summary>Marchează intrarea „Toate nivelurile” din lista de niveluri.</summary>
    private static readonly Tier AnyTier = new() { Id = 0, Name = "Toate nivelurile" };

    private readonly Reward? _existing;

    public RewardWindow(Reward? existing)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _existing = existing;

        LoadTiers();

        if (existing is null)
        {
            TitleText.Text = "Recompensă nouă";
            PointsBox.Text = "100";
            ValueBox.Text = "10,00";
            MinPurchaseBox.Text = "0,00";
            MaxDiscountBox.Text = "0,00";
            TierBox.SelectedIndex = 0;
        }
        else
        {
            TitleText.Text = existing.Name;
            NameBox.Text = existing.Name;
            DescriptionBox.Text = existing.Description ?? string.Empty;
            PointsBox.Text = existing.PointsCost.ToString();
            KindBox.SelectedIndex = (int)existing.Kind - 1;
            ValueBox.Text = AmountParser.Format(existing.Value);
            MinPurchaseBox.Text = AmountParser.Format(existing.MinPurchaseAmount);
            MaxDiscountBox.Text = AmountParser.Format(existing.MaxDiscountAmount);
            FromPicker.SelectedDate = existing.ValidFrom;
            ToPicker.SelectedDate = existing.ValidTo;
            ActiveCheck.IsChecked = existing.IsActive;

            SelectTier(existing.MinTierId);
        }

        UpdateKindHints();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void LoadTiers()
    {
        using var uow = AppServices.Current.Factory.Create();

        var tiers = new List<Tier> { AnyTier };
        tiers.AddRange(uow.Tiers.GetAll());

        TierBox.ItemsSource = tiers;
    }

    private void SelectTier(int? tierId)
    {
        if (TierBox.ItemsSource is not List<Tier> tiers)
            return;

        var match = tierId is null ? AnyTier : tiers.FirstOrDefault(t => t.Id == tierId) ?? AnyTier;
        TierBox.SelectedItem = match;
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => UpdateKindHints();

    /// <summary>Câmpul de valoare înseamnă altceva pentru fiecare tip de recompensă.</summary>
    private void UpdateKindHints()
    {
        if (ValueLabel is null)
            return;

        switch (KindBox.SelectedIndex)
        {
            case 1:
                ValueLabel.Text = "PROCENTUL REDUCERII";
                ValueHint.Text = "De exemplu 10 pentru o reducere de 10% din valoarea bonului.";
                MaxDiscountPanel.Visibility = Visibility.Visible;
                break;

            case 2:
                ValueLabel.Text = "VALOAREA ORIENTATIVĂ A PRODUSULUI (LEI)";
                ValueHint.Text = "Folosită doar pentru evidență. Casierul predă produsul, nu se aplică reducere pe bon.";
                MaxDiscountPanel.Visibility = Visibility.Collapsed;
                break;

            default:
                ValueLabel.Text = "VALOAREA REDUCERII (LEI)";
                ValueHint.Text = "Suma care se scade din bon. Nu poate depăși valoarea bonului.";
                MaxDiscountPanel.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        var name = NameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Denumirea recompensei este obligatorie.");
            return;
        }

        if (!int.TryParse(PointsBox.Text.Trim(), out var points) || points <= 0)
        {
            ShowError("Costul în puncte trebuie să fie un număr întreg mai mare decât zero.");
            return;
        }

        if (!AmountParser.TryParse(ValueBox.Text, out var value) || value <= 0)
        {
            ShowError("Valoarea recompensei trebuie să fie mai mare decât zero.");
            return;
        }

        var kind = (RewardKind)(KindBox.SelectedIndex + 1);

        if (kind == RewardKind.PercentageDiscount && value > 100m)
        {
            ShowError("Reducerea procentuală nu poate depăși 100%.");
            return;
        }

        if (!AmountParser.TryParse(MinPurchaseBox.Text, out var minPurchase) || minPurchase < 0)
        {
            ShowError("Valoarea minimă a bonului trebuie să fie un număr pozitiv.");
            return;
        }

        var maxDiscount = 0m;
        if (kind == RewardKind.PercentageDiscount &&
            (!AmountParser.TryParse(MaxDiscountBox.Text, out maxDiscount) || maxDiscount < 0))
        {
            ShowError("Plafonul reducerii trebuie să fie un număr pozitiv sau zero.");
            return;
        }

        if (FromPicker.SelectedDate is { } from && ToPicker.SelectedDate is { } to && to < from)
        {
            ShowError("Data de sfârșit nu poate fi înaintea datei de început.");
            return;
        }

        var selectedTier = TierBox.SelectedItem as Tier;

        var reward = new Reward
        {
            Id = _existing?.Id ?? 0,
            Name = name,
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
            PointsCost = points,
            Kind = kind,
            Value = value,
            MinPurchaseAmount = minPurchase,
            MaxDiscountAmount = maxDiscount,
            MinTierId = selectedTier is null || selectedTier.Id == 0 ? null : selectedTier.Id,
            IsActive = ActiveCheck.IsChecked == true,
            ValidFrom = FromPicker.SelectedDate,
            ValidTo = ToPicker.SelectedDate,
            SortOrder = _existing?.SortOrder ?? 0,
        };

        var services = AppServices.Current;

        try
        {
            using var uow = services.Factory.Create();

            if (_existing is null)
            {
                reward.SortOrder = uow.Rewards.GetAll().Count + 1;
                reward.Id = uow.Rewards.Insert(reward);
            }
            else
            {
                uow.Rewards.Update(reward);
            }

            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = services.Clock.Now,
                UserId = services.RequireUser.Id,
                UserName = services.RequireUser.Username,
                Action = _existing is null ? "RewardCreated" : "RewardUpdated",
                EntityType = "Reward",
                EntityId = reward.Id,
                Details = $"{reward.Name}: {reward.PointsCost} puncte, {reward.ValueLabel}",
            });

            uow.Commit();
        }
        catch (Exception ex)
        {
            ShowError($"Recompensa nu a putut fi salvată: {ex.Message}");
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
