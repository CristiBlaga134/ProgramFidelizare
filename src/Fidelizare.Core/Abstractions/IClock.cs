namespace Fidelizare.Core.Abstractions;

/// <summary>
/// Sursa de timp a aplicației. Există ca abstracție din două motive: regulile de
/// expirare și bonusul de zi de naștere depind de „acum”, iar acestea trebuie
/// testate fără să se aștepte trecerea timpului real.
///
/// Aplicația lucrează în ora locală a magazinului, nu în UTC. Este o decizie
/// deliberată: rapoartele pe zi trebuie să corespundă zilei de lucru așa cum o
/// percepe personalul, iar programul rulează într-un singur fus orar.
/// </summary>
public interface IClock
{
    DateTime Now { get; }

    DateTime Today => Now.Date;
}

/// <summary>Ceasul folosit în producție.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTime Now => DateTime.Now;
}
