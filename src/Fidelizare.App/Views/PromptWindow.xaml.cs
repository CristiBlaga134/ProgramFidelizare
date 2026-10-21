using System.Windows;
using Fidelizare.App.Infrastructure;

namespace Fidelizare.App.Views;

/// <summary>
/// Cere un text înainte de o acțiune. Folosit pentru motivul anulărilor, care
/// este obligatoriu: o anulare fără explicație nu spune nimic peste o lună,
/// când cineva se uită în jurnal și încearcă să înțeleagă ce s-a întâmplat.
/// </summary>
public partial class PromptWindow : Window
{
    public PromptWindow(string heading, string message, string fieldLabel, string confirmText = "Confirmă")
    {
        InitializeComponent();
        this.FitToWorkArea();

        Title = heading;
        HeadingText.Text = heading;
        MessageText.Text = message;
        FieldLabelText.Text = fieldLabel;
        ConfirmButton.Content = confirmText;

        Loaded += (_, _) => ValueBox.Focus();
    }

    public string Value { get; private set; } = string.Empty;

    /// <summary>Numărul minim de caractere acceptat, ca să nu se scrie doar „x”.</summary>
    public int MinimumLength { get; init; } = 3;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        var text = ValueBox.Text.Trim();

        if (text.Length < MinimumLength)
        {
            ErrorText.Text = $"Explicația trebuie să aibă cel puțin {MinimumLength} caractere.";
            ErrorBanner.Visibility = Visibility.Visible;
            ValueBox.Focus();
            return;
        }

        Value = text;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
