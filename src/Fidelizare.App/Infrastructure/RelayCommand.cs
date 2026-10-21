using System.Windows.Input;

namespace Fidelizare.App.Infrastructure;

/// <summary>
/// Comandă simplă legată de un buton. Fără această clasă ar trebui scrisă o clasă
/// separată pentru fiecare acțiune din interfață.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>
    /// WPF reevaluează disponibilitatea comenzilor la schimbarea focalizării și la
    /// alte evenimente de intrare, deci butoanele se activează singure fără cod în plus.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Forțează reevaluarea imediată, când o schimbare nu vine dintr-o interacțiune.</summary>
    public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
