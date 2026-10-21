using System.Globalization;
using System.Text;

namespace Fidelizare.Data.Sqlite;

/// <summary>
/// Aduce textul la o formă comparabilă: litere mici, fără diacritice, cu spațiile
/// normalizate.
///
/// Este necesar pentru că personalul de la casă tastează repede și fără diacritice.
/// O căutare după „taranu” trebuie să găsească „Țăranu”, altfel clientul e trimis
/// să își caute cardul, ceea ce anulează tot rostul căutării rapide.
/// </summary>
internal static class SearchKey
{
    /// <summary>Perechile pe care descompunerea Unicode nu le rezolvă singură.</summary>
    private static readonly (char From, char To)[] ExtraReplacements =
    [
        ('ș', 's'), ('Ș', 's'), ('ş', 's'), ('Ş', 's'),
        ('ț', 't'), ('Ț', 't'), ('ţ', 't'), ('Ţ', 't'),
        ('ß', 's'), ('đ', 'd'), ('Đ', 'd'),
    ];

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var replaced = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var mapped = c;
            foreach (var (from, to) in ExtraReplacements)
            {
                if (c == from)
                {
                    mapped = to;
                    break;
                }
            }

            replaced.Append(mapped);
        }

        // Descompunerea separă litera de semnul diacritic, iar semnele se pot
        // atunci elimina: „ă” devine „a” + accent, din care rămâne „a”.
        var decomposed = replaced.ToString().Normalize(NormalizationForm.FormD);

        var result = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && result.Length > 0)
                    result.Append(' ');

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>Cheia de căutare a unui client, din toate câmpurile după care s-ar putea căuta.</summary>
    public static string ForCustomer(string lastName, string firstName, string? phone, string? email, string cardCode) =>
        Normalize($"{lastName} {firstName} {phone} {email} {cardCode}");
}
