using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;

namespace Fidelizare.App.Views;

/// <summary>
/// Partea din formularul de client care construiește, la deschidere, câmpurile
/// definite de magazin.
///
/// Controalele nu pot fi scrise în XAML, pentru că nu se știe dinainte nici câte
/// sunt, nici ce tip au: fiecare magazin își definește propriile câmpuri. Se
/// generează aici, o dată per deschidere a ferestrei.
/// </summary>
public partial class CustomerWindow
{
    /// <summary>Controlul de editare al fiecărui câmp, pe identificatorul definiției.</summary>
    private readonly Dictionary<int, FrameworkElement> _fieldControls = [];

    private IReadOnlyList<CustomFieldDefinition> _fields = [];

    private void BuildCustomFields(Customer? existing)
    {
        _fields = AppServices.Current.Loyalty.GetCustomFields();

        // Clientul primit poate veni din lista de clienți, care nu încarcă și
        // valorile câmpurilor proprii. Reluat de aici, formularul le arată pe
        // cele reale — altfel ar apărea goale, iar salvarea le-ar șterge.
        if (existing is not null)
            existing = AppServices.Current.Loyalty.GetCustomer(existing.Id) ?? existing;

        CustomFieldsPanel.Children.Clear();
        _fieldControls.Clear();

        if (_fields.Count == 0)
            return;

        foreach (var field in _fields)
        {
            var label = new TextBlock
            {
                Text = (field.Label + (field.IsRequired ? " *" : string.Empty)).ToUpperInvariant(),
                Style = (Style)FindResource("FieldLabel"),
                Margin = new Thickness(0, 14, 0, 4),
            };

            CustomFieldsPanel.Children.Add(label);

            var current = existing is not null && existing.CustomValues.TryGetValue(field.Id, out var stored)
                ? stored
                : null;

            var control = CreateControl(field, current);
            _fieldControls[field.Id] = control;
            CustomFieldsPanel.Children.Add(control);
        }
    }

    private FrameworkElement CreateControl(CustomFieldDefinition field, string? value) => field.Kind switch
    {
        CustomFieldKind.YesNo => new CheckBox
        {
            Content = field.Label,
            IsChecked = string.Equals(value, "da", StringComparison.OrdinalIgnoreCase),
            Margin = new Thickness(0, 2, 0, 0),
        },

        CustomFieldKind.Date => new DatePicker
        {
            SelectedDate = CsvDate(value),
        },

        CustomFieldKind.Choice => BuildChoice(field, value),

        _ => new TextBox { Text = value ?? string.Empty },
    };

    private static ComboBox BuildChoice(CustomFieldDefinition field, string? value)
    {
        var box = new ComboBox { IsEditable = false };

        // Prima intrare goală există ca să se poată reveni la „necompletat” pe un
        // câmp care nu este obligatoriu.
        if (!field.IsRequired)
            box.Items.Add(string.Empty);

        foreach (var option in field.ChoiceList)
            box.Items.Add(option);

        box.SelectedItem = field.ChoiceList.FirstOrDefault(
            o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase));

        return box;
    }

    /// <summary>Citește valorile completate, în forma în care se salvează în baza de date.</summary>
    private Dictionary<int, string?> ReadCustomFields()
    {
        var values = new Dictionary<int, string?>();

        foreach (var field in _fields)
        {
            if (!_fieldControls.TryGetValue(field.Id, out var control))
                continue;

            values[field.Id] = control switch
            {
                CheckBox check => check.IsChecked == true ? "da" : null,
                DatePicker picker => picker.SelectedDate?.ToString("dd.MM.yyyy"),
                ComboBox combo => combo.SelectedItem as string,
                TextBox text => string.IsNullOrWhiteSpace(text.Text) ? null : text.Text.Trim(),
                _ => null,
            };
        }

        return values;
    }

    private static DateTime? CsvDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
}
