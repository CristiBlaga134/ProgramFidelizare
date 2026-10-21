namespace Fidelizare.Core.Model;

/// <summary>
/// Un nivel de fidelitate. Clientul urcă în funcție de rulajul din perioada de
/// calificare și primește un multiplicator mai bun la acumulare.
/// </summary>
public sealed class Tier
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Rulajul minim, în lei, pentru a intra pe acest nivel.</summary>
    public decimal Threshold { get; set; }

    /// <summary>Cu cât se înmulțesc punctele acordate la acest nivel (ex. 1,25).</summary>
    public decimal EarnMultiplier { get; set; } = 1m;

    /// <summary>Reducere permanentă acordată la orice cumpărare, în procente. Poate fi 0.</summary>
    public decimal StandingDiscountPercent { get; set; }

    /// <summary>Culoare pentru interfață, în format #RRGGBB.</summary>
    public string ColorHex { get; set; } = "#6B7280";

    /// <summary>Ordinea de afișare și de comparare a nivelurilor, crescător.</summary>
    public int SortOrder { get; set; }

    public string? Benefits { get; set; }
}
