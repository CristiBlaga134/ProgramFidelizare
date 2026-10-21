using System.Windows;
using System.Windows.Controls;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Model;

namespace Fidelizare.App.Views;

/// <summary>Definirea unui câmp propriu pe fișa clientului.</summary>
public partial class CustomFieldWindow : Window
{
    private readonly CustomFieldDefinition? _existing;

    /// <summary>
    /// Numele scurt se completează singur din etichetă doar cât timp nu a fost
    /// atins de utilizator. Odată modificat manual, rămâne așa: altfel un nume
    /// ales cu grijă ar fi rescris la următoarea corectură a etichetei.
    /// </summary>
    private bool _keyEditedManually;

    public CustomFieldWindow(CustomFieldDefinition? existing)
    {
        InitializeComponent();
        this.FitToWorkArea();

        _existing = existing;

        if (existing is null)
        {
            TitleText.Text = "Câmp nou";
        }
        else
        {
            TitleText.Text = existing.Label;
            LabelBox.Text = existing.Label;
            KeyBox.Text = existing.Key;
            KindBox.SelectedIndex = (int)existing.Kind - 1;
            OptionsBox.Text = existing.Options ?? string.Empty;
            RequiredCheck.IsChecked = existing.IsRequired;
            SearchCheck.IsChecked = existing.IncludeInSearch;
            ListCheck.IsChecked = existing.ShowInList;
            ActiveCheck.IsChecked = existing.IsActive;

            _keyEditedManually = true;
        }

        UpdateOptionsVisibility();
        Loaded += (_, _) => LabelBox.Focus();
    }

    private void OnLabelChanged(object sender, TextChangedEventArgs e)
    {
        if (_keyEditedManually || KeyBox is null)
            return;

        KeyBox.Text = CustomFieldDefinition.SuggestKey(LabelBox.Text);
    }

    /// <summary>Prima tastă apăsată în câmp oprește completarea automată.</summary>
    private void OnKeyTyped(object sender, System.Windows.Input.KeyEventArgs e) => _keyEditedManually = true;

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => UpdateOptionsVisibility();

    private void UpdateOptionsVisibility()
    {
        if (OptionsPanel is null)
            return;

        OptionsPanel.Visibility = KindBox.SelectedIndex == (int)CustomFieldKind.Choice - 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        var definition = new CustomFieldDefinition
        {
            Id = _existing?.Id ?? 0,
            Label = LabelBox.Text.Trim(),
            Key = KeyBox.Text.Trim(),
            Kind = (CustomFieldKind)(KindBox.SelectedIndex + 1),
            Options = string.IsNullOrWhiteSpace(OptionsBox.Text) ? null : OptionsBox.Text.Trim(),
            IsRequired = RequiredCheck.IsChecked == true,
            IncludeInSearch = SearchCheck.IsChecked == true,
            ShowInList = ListCheck.IsChecked == true,
            IsActive = ActiveCheck.IsChecked == true,
            SortOrder = _existing?.SortOrder ?? 0,
        };

        var errors = definition.ValidateDefinition();

        if (errors.Count > 0)
        {
            ShowError(string.Join(Environment.NewLine, errors));
            return;
        }

        var services = AppServices.Current;

        try
        {
            using var uow = services.Factory.Create();

            if (uow.CustomFields.KeyExists(definition.Key, definition.Id == 0 ? null : definition.Id))
            {
                ShowError($"Există deja un câmp cu numele scurt „{definition.Key}”.");
                return;
            }

            if (_existing is null)
            {
                definition.SortOrder = uow.CustomFields.GetDefinitions(includeInactive: true).Count + 1;
                definition.Id = uow.CustomFields.InsertDefinition(definition);
            }
            else
            {
                uow.CustomFields.UpdateDefinition(definition);
            }

            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = services.Clock.Now,
                UserId = services.RequireUser.Id,
                UserName = services.RequireUser.Username,
                Action = _existing is null ? "CustomFieldCreated" : "CustomFieldUpdated",
                EntityType = "CustomField",
                EntityId = definition.Id,
                Details = $"{definition.Label} ({definition.Key}, {definition.KindLabel})",
            });

            uow.Commit();
        }
        catch (Exception ex)
        {
            ShowError($"Câmpul nu a putut fi salvat: {ex.Message}");
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
