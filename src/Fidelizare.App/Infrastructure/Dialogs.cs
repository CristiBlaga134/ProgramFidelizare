using System.Windows;

namespace Fidelizare.App.Infrastructure;

/// <summary>
/// Mesajele către utilizator, într-un singur loc, ca titlul și tonul să fie
/// aceleași peste tot în aplicație.
/// </summary>
public static class Dialogs
{
    private const string Title = "Program de fidelizare";

    public static void Info(string message) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warn(string message) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Error(string message) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>Confirmare pentru acțiuni obișnuite. Implicit „Nu”, ca un Enter grăbit să nu confirme.</summary>
    public static bool Confirm(string message) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>Confirmare pentru acțiuni care schimbă solduri sau șterg date.</summary>
    public static bool ConfirmDestructive(string message) =>
        MessageBox.Show(Owner(), message, Title, MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>
    /// Fereastra activă, ca mesajul să apară peste ea și să o blocheze.
    /// Fără proprietar, un mesaj poate ajunge în spatele ferestrei principale.
    /// </summary>
    private static Window Owner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow!;
}
