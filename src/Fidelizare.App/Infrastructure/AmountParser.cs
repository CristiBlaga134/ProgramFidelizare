using System.Globalization;

namespace Fidelizare.App.Infrastructure;

/// <summary>
/// Citirea sumelor tastate de casier.
///
/// La casă se tastează repede și de pe tastaturi diferite: pe cea numerică punctul
/// este mai la îndemână decât virgula, dar setările românești așteaptă virgulă.
/// Ambele trebuie să funcționeze, altfel „199.90” ar fi citit ca 19990 lei —
/// o greșeală care s-ar vedea abia în raportul de la sfârșitul zilei.
/// </summary>
public static class AmountParser
{
    /// <summary>Încearcă să citească o sumă, acceptând atât virgula cât și punctul ca separator zecimal.</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty);

        // Dacă apar ambele semne, ultimul este cel zecimal, iar celălalt separă miile.
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            var decimalSeparator = lastComma > lastDot ? ',' : '.';
            var thousandsSeparator = decimalSeparator == ',' ? '.' : ',';

            cleaned = cleaned.Replace(thousandsSeparator.ToString(), string.Empty);
            cleaned = cleaned.Replace(decimalSeparator, '.');
        }
        else
        {
            cleaned = cleaned.Replace(',', '.');
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Suma citită, sau zero dacă textul nu este un număr.</summary>
    public static decimal ParseOrZero(string? text) => TryParse(text, out var value) ? value : 0m;

    /// <summary>Formatarea pentru câmpurile editabile: fără separator de mii, ca textul să poată fi recitit.</summary>
    public static string Format(decimal value) => value.ToString("0.00", CultureInfo.CurrentCulture);
}
