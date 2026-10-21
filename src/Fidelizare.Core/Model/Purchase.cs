namespace Fidelizare.Core.Model;

/// <summary>
/// O cumpărare înregistrată la casă. Punctele se acordă pe suma net încasată,
/// deci reducerea revendicată din puncte nu generează puncte noi.
/// </summary>
public sealed class Purchase
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    /// <summary>Numărul bonului fiscal, ca să se poată corela cu casa de marcat.</summary>
    public string? ReceiptNumber { get; set; }

    /// <summary>Valoarea coșului înainte de orice reducere.</summary>
    public decimal GrossAmount { get; set; }

    /// <summary>Reducerea acordată din recompense sau comercială.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>Suma efectiv încasată. Baza de calcul pentru puncte și pentru nivel.</summary>
    public decimal NetAmount { get; set; }

    /// <summary>Punctele acordate, după aplicarea multiplicatorului de nivel.</summary>
    public int PointsEarned { get; set; }

    /// <summary>Nivelul clientului în momentul cumpărării, păstrat pentru urmărire.</summary>
    public string TierAtPurchase { get; set; } = string.Empty;

    /// <summary>Multiplicatorul aplicat, păstrat ca să se poată reface calculul.</summary>
    public decimal MultiplierApplied { get; set; } = 1m;

    /// <summary>Campania activă în momentul vânzării, dacă a existat una.</summary>
    public string? CampaignName { get; set; }

    /// <summary>Multiplicatorul adus de campanie. 1 înseamnă că nu s-a aplicat niciuna.</summary>
    public decimal CampaignMultiplier { get; set; } = 1m;

    public DateTime OccurredAt { get; set; }

    /// <summary>Utilizatorul care a înregistrat operațiunea.</summary>
    public int? RecordedByUserId { get; set; }

    public string? RecordedByUserName { get; set; }

    public bool IsVoided { get; set; }

    public DateTime? VoidedAt { get; set; }

    public string? VoidReason { get; set; }
}
