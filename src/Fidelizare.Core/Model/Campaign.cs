using System.Globalization;

namespace Fidelizare.Core.Model;

/// <summary>
/// O perioadă cu acumulare accelerată: „puncte duble în weekendul de Black Friday”,
/// „×1,5 toată luna decembrie”, „puncte triple marțea”.
///
/// Campania se aplică peste multiplicatorul de nivel al clientului. Dacă mai
/// multe campanii sunt valabile în același moment, se ia cea mai generoasă, nu
/// produsul lor: două campanii de ×2 suprapuse din greșeală ar însemna ×4, adică
/// exact genul de accident care se observă abia în raportul de la sfârșitul lunii.
/// </summary>
public sealed class Campaign
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Prima zi în care campania se aplică, inclusiv.</summary>
    public DateTime StartsOn { get; set; }

    /// <summary>Ultima zi în care campania se aplică, inclusiv.</summary>
    public DateTime EndsOn { get; set; }

    /// <summary>Cu cât se înmulțesc punctele. 2 înseamnă puncte duble.</summary>
    public decimal Multiplier { get; set; } = 2m;

    /// <summary>Valoarea minimă a bonului pentru care campania se aplică. 0 = fără condiție.</summary>
    public decimal MinPurchaseAmount { get; set; }

    /// <summary>
    /// Zilele săptămânii în care se aplică, ca mască de biți: bitul 0 este luni,
    /// bitul 6 duminică. Zero înseamnă toate zilele.
    /// </summary>
    public int DaysOfWeekMask { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Verifică dacă o cumpărare de o anumită valoare, la un anumit moment, intră în campanie.</summary>
    public bool AppliesTo(DateTime moment, decimal netAmount)
    {
        if (!IsActive || Multiplier <= 0)
            return false;

        var day = moment.Date;
        if (day < StartsOn.Date || day > EndsOn.Date)
            return false;

        if (DaysOfWeekMask != 0 && (DaysOfWeekMask & (1 << BitFor(day.DayOfWeek))) == 0)
            return false;

        return netAmount >= MinPurchaseAmount;
    }

    /// <summary>Adevărat dacă perioada campaniei include ziua dată, indiferent de valoarea bonului.</summary>
    public bool IsRunningOn(DateTime day) =>
        IsActive && day.Date >= StartsOn.Date && day.Date <= EndsOn.Date;

    /// <summary>Luni este primul bit, ca în calendarul românesc.</summary>
    public static int BitFor(DayOfWeek day) => day == DayOfWeek.Sunday ? 6 : (int)day - 1;

    private static readonly string[] DayNames =
        ["luni", "marți", "miercuri", "joi", "vineri", "sâmbătă", "duminică"];

    public string DaysLabel
    {
        get
        {
            if (DaysOfWeekMask == 0)
                return "toate zilele";

            var names = new List<string>();
            for (var bit = 0; bit < 7; bit++)
            {
                if ((DaysOfWeekMask & (1 << bit)) != 0)
                    names.Add(DayNames[bit]);
            }

            return names.Count == 0 ? "nicio zi" : string.Join(", ", names);
        }
    }

    public string PeriodLabel =>
        $"{StartsOn.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)} – " +
        $"{EndsOn.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)}";

    public string MultiplierLabel => $"×{Multiplier.ToString("0.##", CultureInfo.CurrentCulture)}";
}
