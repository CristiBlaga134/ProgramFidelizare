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

    /// <summary>Suma returnată până acum din acest bon, cumulat din toate retururile.</summary>
    public decimal ReturnedAmount { get; set; }

    /// <summary>
    /// Punctele socotite deja retrase prin retururi, inclusiv cele care nu au mai
    /// putut fi luate din sold fiindcă fuseseră cheltuite. Ține evidența, ca două
    /// retururi pe același bon să nu retragă de două ori aceleași puncte.
    /// </summary>
    public int PointsReturned { get; set; }

    /// <summary>Cât a rămas din bon după retururi. Baza pentru un retur următor.</summary>
    public decimal RemainingAmount => Math.Max(0m, NetAmount - ReturnedAmount);

    public bool HasReturns => ReturnedAmount > 0;

    /// <summary>Starea bonului, pentru coloana din fișa clientului.</summary>
    public string StatusLabel =>
        IsVoided && HasReturns && RemainingAmount == 0 ? "Returnată integral"
        : IsVoided ? "Anulată"
        : HasReturns ? $"Retur {ReturnedAmount:N2} lei"
        : string.Empty;
}
