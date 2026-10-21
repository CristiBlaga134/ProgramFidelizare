namespace Fidelizare.Core.Model;

/// <summary>
/// Revendicarea unei recompense: punctele au fost consumate și clientul a primit beneficiul.
/// Datele recompensei se copiază aici, ca istoricul să rămână corect chiar dacă
/// recompensa este ulterior modificată sau scoasă din catalog.
/// </summary>
public sealed class Redemption
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public int RewardId { get; set; }

    /// <summary>Numele recompensei la momentul revendicării.</summary>
    public string RewardName { get; set; } = string.Empty;

    public RewardKind Kind { get; set; }

    public int PointsSpent { get; set; }

    /// <summary>Valoarea reducerii calculată pentru coșul respectiv, în lei.</summary>
    public decimal DiscountValue { get; set; }

    /// <summary>Valoarea coșului pentru care s-a aplicat, dacă a fost legată de o vânzare.</summary>
    public decimal? BasketAmount { get; set; }

    public string? ReceiptNumber { get; set; }

    /// <summary>Mișcarea din registru care a consumat punctele. Prin ea se ajunge la loturile atinse.</summary>
    public int PointTransactionId { get; set; }

    public DateTime OccurredAt { get; set; }

    public int? UserId { get; set; }
    public string? UserName { get; set; }

    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
}
