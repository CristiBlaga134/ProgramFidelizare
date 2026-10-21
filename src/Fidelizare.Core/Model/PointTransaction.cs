namespace Fidelizare.Core.Model;

/// <summary>
/// O linie din registrul de puncte. Registrul este append-only și reprezintă
/// istoricul complet al contului: din el se poate reconstitui oricând cum s-a
/// ajuns la soldul actual, ceea ce contează când un client contestă un sold.
/// </summary>
public sealed class PointTransaction
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public PointTransactionType Type { get; set; }

    /// <summary>Pozitiv la acordare și restituire, negativ la consum și expirare.</summary>
    public int Points { get; set; }

    /// <summary>Soldul rezultat după această mișcare, ca să se poată verifica ușor istoricul.</summary>
    public int BalanceAfter { get; set; }

    public int? PointLotId { get; set; }
    public int? PurchaseId { get; set; }
    public int? RedemptionId { get; set; }

    public DateTime OccurredAt { get; set; }

    public int? UserId { get; set; }
    public string? UserName { get; set; }

    /// <summary>Explicație în limbaj natural, afișată în fișa clientului.</summary>
    public string? Note { get; set; }

    /// <summary>Text scurt pentru interfață, în funcție de tipul mișcării.</summary>
    public string TypeLabel => Type switch
    {
        PointTransactionType.Earned => "Puncte acordate",
        PointTransactionType.Redeemed => "Recompensă revendicată",
        PointTransactionType.Expired => "Puncte expirate",
        PointTransactionType.AdjustmentPlus => "Corecție în plus",
        PointTransactionType.AdjustmentMinus => "Corecție în minus",
        PointTransactionType.ReversalOfEarning => "Anulare cumpărare",
        PointTransactionType.ReversalOfRedemption => "Anulare recompensă",
        PointTransactionType.Welcome => "Bonus de înscriere",
        PointTransactionType.Birthday => "Bonus zi de naștere",
        PointTransactionType.Return => "Retur produse",
        _ => Type.ToString(),
    };
}
