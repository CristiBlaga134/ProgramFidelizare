namespace Fidelizare.Core.Model;

/// <summary>
/// O recompensă din catalog, pe care clientul o poate obține în schimbul punctelor.
/// </summary>
public sealed class Reward
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Câte puncte costă.</summary>
    public int PointsCost { get; set; }

    public RewardKind Kind { get; set; }

    /// <summary>
    /// Lei pentru reducere fixă, procente pentru reducere procentuală,
    /// iar pentru produs gratuit valoarea orientativă a produsului.
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>Valoarea minimă a coșului pentru care recompensa poate fi folosită. 0 = fără condiție.</summary>
    public decimal MinPurchaseAmount { get; set; }

    /// <summary>Plafon al reducerii pentru recompensele procentuale, în lei. 0 = fără plafon.</summary>
    public decimal MaxDiscountAmount { get; set; }

    /// <summary>Nivelul minim necesar. Null înseamnă disponibilă tuturor.</summary>
    public int? MinTierId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Descriere scurtă a beneficiului, pentru listele din interfață.</summary>
    public string ValueLabel => Kind switch
    {
        RewardKind.FixedAmountDiscount => $"-{Value:0.##} lei",
        RewardKind.PercentageDiscount => MaxDiscountAmount > 0
            ? $"-{Value:0.##}% (max {MaxDiscountAmount:0.##} lei)"
            : $"-{Value:0.##}%",
        RewardKind.FreeProduct => "Produs gratuit",
        _ => string.Empty,
    };
}
