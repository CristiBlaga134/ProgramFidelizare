using System.Windows;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;

namespace Fidelizare.App.Views;

/// <summary>
/// Formularul de înscriere și de modificare a unui client.
///
/// Aceeași fereastră servește ambele scopuri: câmpurile sunt identice, iar
/// diferența este doar dacă la salvare se creează un client nou sau se
/// actualizează unul existent.
/// </summary>
public partial class CustomerWindow : Window
{
    private readonly Customer? _existing;

    public CustomerWindow(Customer? existing = null)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _existing = existing;

        if (existing is null)
        {
            var rules = AppServices.Current.Loyalty.GetRules();

            TitleText.Text = "Client nou";
            SubtitleText.Text = rules.WelcomeBonusPoints > 0
                ? $"La înscriere clientul primește {rules.WelcomeBonusPoints} puncte."
                : "Completează datele clientului.";

            SaveButton.Content = "Înscrie clientul";
        }
        else
        {
            TitleText.Text = "Modifică datele clientului";
            SubtitleText.Text = $"Înscris pe {existing.EnrolledAt:dd.MM.yyyy}.";
            SaveButton.Content = "Salvează";
            ActiveCheck.Visibility = Visibility.Visible;

            LastNameBox.Text = existing.LastName;
            FirstNameBox.Text = existing.FirstName;
            PhoneBox.Text = existing.Phone ?? string.Empty;
            EmailBox.Text = existing.Email ?? string.Empty;
            CardCodeBox.Text = existing.CardCode;
            NotesBox.Text = existing.Notes ?? string.Empty;
            BirthDatePicker.SelectedDate = existing.BirthDate;
            ConsentCheck.IsChecked = existing.MarketingConsent;
            ActiveCheck.IsChecked = existing.IsActive;
        }

        BuildCustomFields(existing);

        Loaded += (_, _) => LastNameBox.Focus();
    }

    /// <summary>Clientul rezultat, disponibil după închiderea cu succes a ferestrei.</summary>
    public Customer? SavedCustomer { get; private set; }

    private void OnGenerateCard(object sender, RoutedEventArgs e)
    {
        try
        {
            CardCodeBox.Text = AppServices.Current.Loyalty.GenerateCardCode();
        }
        catch (Exception ex)
        {
            ShowError($"Codul nu a putut fi generat: {ex.Message}");
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        var services = AppServices.Current;

        var customer = new Customer
        {
            Id = _existing?.Id ?? 0,
            LastName = LastNameBox.Text.Trim(),
            FirstName = FirstNameBox.Text.Trim(),
            Phone = PhoneBox.Text.Trim(),
            Email = EmailBox.Text.Trim(),
            CardCode = CardCodeBox.Text.Trim(),
            Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            BirthDate = BirthDatePicker.SelectedDate,
            MarketingConsent = ConsentCheck.IsChecked == true,
            IsActive = _existing is null || ActiveCheck.IsChecked == true,
            EnrolledAt = _existing?.EnrolledAt ?? services.Clock.Now,
            ConsentAt = _existing?.ConsentAt,
        };

        foreach (var (fieldId, value) in ReadCustomFields())
            customer.CustomValues[fieldId] = value;

        if (_existing is null)
        {
            var result = services.Loyalty.EnrollCustomer(customer, services.RequireUser);

            if (result.Failed)
            {
                ShowError(result.Error!);
                return;
            }

            SavedCustomer = result.Value;
        }
        else
        {
            var result = services.Loyalty.UpdateCustomer(customer, services.RequireUser);

            if (result.Failed)
            {
                ShowError(result.Error!);
                return;
            }

            SavedCustomer = customer;
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
