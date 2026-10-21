namespace Fidelizare.Core.Model;

/// <summary>
/// Urmă a unei operațiuni sensibile: corecții de puncte, anulări, modificări de
/// reguli, gestiune de utilizatori. Serveşte la a putea răspunde la întrebarea
/// „cine a făcut asta și când”, inevitabilă când se lucrează cu beneficii.
/// </summary>
public sealed class AuditEntry
{
    public int Id { get; set; }

    public DateTime OccurredAt { get; set; }

    public int? UserId { get; set; }
    public string? UserName { get; set; }

    /// <summary>Acțiunea, în formă scurtă și stabilă (ex. „PurchaseVoided”).</summary>
    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }
    public int? EntityId { get; set; }

    /// <summary>Detalii lizibile, inclusiv valorile de dinainte și de după.</summary>
    public string? Details { get; set; }
}
