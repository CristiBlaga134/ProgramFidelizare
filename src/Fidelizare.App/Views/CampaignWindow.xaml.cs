using System.Windows;
using System.Windows.Controls;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;

namespace Fidelizare.App.Views;

/// <summary>Adăugarea sau modificarea unei campanii promoționale.</summary>
public partial class CampaignWindow : Window
{
    private readonly Campaign? _existing;

    public CampaignWindow(Campaign? existing)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _existing = existing;

        var today = AppServices.Current.Clock.Today;

        if (existing is null)
        {
            TitleText.Text = "Campanie nouă";
            FromPicker.SelectedDate = today;
            ToPicker.SelectedDate = today.AddDays(2);
            MultiplierBox.Text = "2";
            MinPurchaseBox.Text = "0,00";
        }
        else
        {
            TitleText.Text = existing.Name;
            NameBox.Text = existing.Name;
            DescriptionBox.Text = existing.Description ?? string.Empty;
            FromPicker.SelectedDate = existing.StartsOn;
            ToPicker.SelectedDate = existing.EndsOn;
            MultiplierBox.Text = existing.Multiplier.ToString("0.##");
            MinPurchaseBox.Text = AmountParser.Format(existing.MinPurchaseAmount);
            ActiveCheck.IsChecked = existing.IsActive;

            ApplyDaysMask(existing.DaysOfWeekMask);
        }

        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnAllDaysChanged(object sender, RoutedEventArgs e) =>
        DaysPanel.IsEnabled = AllDaysCheck.IsChecked != true;

    private void ApplyDaysMask(int mask)
    {
        AllDaysCheck.IsChecked = mask == 0;
        DaysPanel.IsEnabled = mask != 0;

        foreach (var (box, day) in DayBoxes())
            box.IsChecked = (mask & (1 << Campaign.BitFor(day))) != 0;
    }

    private int ReadDaysMask()
    {
        if (AllDaysCheck.IsChecked == true)
            return 0;

        var mask = 0;

        foreach (var (box, day) in DayBoxes())
        {
            if (box.IsChecked == true)
                mask |= 1 << Campaign.BitFor(day);
        }

        return mask;
    }

    private IEnumerable<(CheckBox Box, DayOfWeek Day)> DayBoxes()
    {
        yield return (MondayCheck, DayOfWeek.Monday);
        yield return (TuesdayCheck, DayOfWeek.Tuesday);
        yield return (WednesdayCheck, DayOfWeek.Wednesday);
        yield return (ThursdayCheck, DayOfWeek.Thursday);
        yield return (FridayCheck, DayOfWeek.Friday);
        yield return (SaturdayCheck, DayOfWeek.Saturday);
        yield return (SundayCheck, DayOfWeek.Sunday);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        var name = NameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Denumirea campaniei este obligatorie.");
            return;
        }

        if (FromPicker.SelectedDate is not { } from || ToPicker.SelectedDate is not { } to)
        {
            ShowError("Completează ambele date ale perioadei.");
            return;
        }

        if (to.Date < from.Date)
        {
            ShowError("Data de sfârșit nu poate fi înaintea celei de început.");
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

        if (!AmountParser.TryParse(MinPurchaseBox.Text, out var minPurchase) || minPurchase < 0)
        {
            ShowError("Valoarea minimă a bonului trebuie să fie un număr pozitiv.");
            return;
        }

        var mask = ReadDaysMask();

        if (AllDaysCheck.IsChecked != true && mask == 0)
        {
            ShowError("Alege cel puțin o zi a săptămânii, sau bifează „În toate zilele”.");
            return;
        }

        var campaign = new Campaign
        {
            Id = _existing?.Id ?? 0,
            Name = name,
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
            StartsOn = from.Date,
            EndsOn = to.Date,
            Multiplier = multiplier,
            MinPurchaseAmount = minPurchase,
            DaysOfWeekMask = mask,
            IsActive = ActiveCheck.IsChecked == true,
        };

        var services = AppServices.Current;

        try
        {
            using var uow = services.Factory.Create();

            if (_existing is null)
                campaign.Id = uow.Campaigns.Insert(campaign);
            else
                uow.Campaigns.Update(campaign);

            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = services.Clock.Now,
                UserId = services.RequireUser.Id,
                UserName = services.RequireUser.Username,
                Action = _existing is null ? "CampaignCreated" : "CampaignUpdated",
                EntityType = "Campaign",
                EntityId = campaign.Id,
                Details = $"{campaign.Name}: {campaign.PeriodLabel}, {campaign.MultiplierLabel}, {campaign.DaysLabel}",
            });

            uow.Commit();
        }
        catch (Exception ex)
        {
            ShowError($"Campania nu a putut fi salvată: {ex.Message}");
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
