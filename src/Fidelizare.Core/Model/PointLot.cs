namespace Fidelizare.Core.Model;

/// <summary>
/// Un pachet de puncte acordat la un moment dat, cu propria dată de expirare.
/// Soldul clientului este suma punctelor rămase din loturile neexpirate.
///
/// Motivul acestei structuri: punctele expiră individual, la un an de la acordare.
/// Un simplu câmp „sold” nu ar permite să se știe care puncte expiră și când,
/// iar consumul se face în ordinea expirării, ca clientul să nu piardă puncte degeaba.
/// </summary>
public sealed class PointLot
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public PointLotSource Source { get; set; }

    /// <summary>Cumpărarea care a generat lotul, dacă provine dintr-o cumpărare.</summary>
    public int? PurchaseId { get; set; }

    /// <summary>Câte puncte a avut lotul la creare. Nu se modifică niciodată.</summary>
    public int PointsOriginal { get; set; }

    /// <summary>Câte puncte mai sunt disponibile din lot. Scade la consum și la expirare.</summary>
    public int PointsRemaining { get; set; }

    public DateTime EarnedAt { get; set; }

    /// <summary>Momentul expirării. Null înseamnă puncte care nu expiră.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Marcat când procesul de expirare a consumat restul lotului.</summary>
    public bool IsExpired { get; set; }

    public bool HasPointsLeft => PointsRemaining > 0 && !IsExpired;
}
