namespace Fidelizare.Core.Model;

/// <summary>
/// Un retur de produse dintr-un bon. Un bon poate avea mai multe retururi, câte
/// unul pentru fiecare vizită a clientului; suma lor nu poate depăși bonul.
///
/// Programul nu cunoaște produsele de pe bon, doar totalul, deci returul se
/// consemnează ca sumă: valoarea produselor aduse înapoi, așa cum apare pe
/// documentul de retur al casei de marcat.
/// </summary>
public sealed class PurchaseReturn
{
    public int Id { get; set; }

    public int PurchaseId { get; set; }

    public int CustomerId { get; set; }

    /// <summary>Valoarea produselor returnate, în lei.</summary>
    public decimal Amount { get; set; }

    /// <summary>Punctele care trebuiau retrase pentru acest retur.</summary>
    public int PointsReversed { get; set; }

    /// <summary>Din punctele de retras, câte nu mai erau în sold, fiind deja cheltuite.</summary>
    public int PointsShortfall { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; }

    public int? UserId { get; set; }

    public string? UserName { get; set; }
}
