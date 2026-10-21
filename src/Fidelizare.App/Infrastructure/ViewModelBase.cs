using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Fidelizare.App.Infrastructure;

/// <summary>
/// Bază pentru modelele de vizualizare: anunță interfața când o valoare se schimbă.
/// Se scrie o singură dată aici, ca ecranele să conțină doar logica lor.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Scrie valoarea și anunță schimbarea doar dacă valoarea chiar s-a schimbat.
    /// Verificarea evită reîmprospătări inutile și cicluri de actualizare.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Anunță schimbarea mai multor proprietăți calculate deodată.</summary>
    protected void OnPropertyChanged(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
            OnPropertyChanged(name);
    }
}
