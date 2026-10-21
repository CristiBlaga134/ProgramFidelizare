using System.Globalization;

namespace Fidelizare.Core.Model;

/// <summary>
/// Un câmp adăugat de magazin pe fișa clientului: mărime, stil preferat, oraș,
/// cum a aflat de program.
///
/// Fiecare magazin are nevoie de alte informații, iar acestea nu pot fi ghicite
/// în avans. Definițiile stau în baza de date, nu în cod, deci un magazin nou
/// nu are nevoie de o versiune nouă a programului ca să își noteze ce îl interesează.
/// </summary>
public sealed class CustomFieldDefinition
{
    public int Id { get; set; }

    /// <summary>Textul afișat lângă câmp, în fișa clientului.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Numele scurt, stabil, folosit ca antet la export. Rămâne neschimbat chiar
    /// dacă eticheta afișată este redenumită, ca fișierele exportate anterior să
    /// rămână comparabile.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public CustomFieldKind Kind { get; set; } = CustomFieldKind.Text;

    /// <summary>Variantele pentru câmpurile de tip alegere, câte una pe linie.</summary>
    public string? Options { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>Dacă apare ca o coloană în lista de clienți.</summary>
    public bool ShowInList { get; set; }

    /// <summary>Dacă valoarea intră în căutarea rapidă de la casă.</summary>
    public bool IncludeInSearch { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>Un câmp dezactivat nu mai apare în formular, dar valorile deja completate rămân.</summary>
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<string> ChoiceList =>
        string.IsNullOrWhiteSpace(Options)
            ? []
            : Options.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string KindLabel => Kind switch
    {
        CustomFieldKind.Text => "Text",
        CustomFieldKind.Number => "Număr",
        CustomFieldKind.Date => "Dată",
        CustomFieldKind.YesNo => "Da / Nu",
        CustomFieldKind.Choice => "Listă de opțiuni",
        _ => Kind.ToString(),
    };

    /// <summary>Verifică o valoare introdusă. Întoarce mesajul de eroare sau null dacă e bună.</summary>
    public string? Validate(string? value)
    {
        var text = value?.Trim();

        if (string.IsNullOrEmpty(text))
            return IsRequired ? $"Câmpul „{Label}” este obligatoriu." : null;

        switch (Kind)
        {
            case CustomFieldKind.Number:
                if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out _) &&
                    !decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    return $"Câmpul „{Label}” trebuie să conțină un număr.";
                break;

            case CustomFieldKind.Date:
                if (!DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out _) &&
                    !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    return $"Câmpul „{Label}” trebuie să conțină o dată.";
                break;

            case CustomFieldKind.Choice:
                if (ChoiceList.Count > 0 && !ChoiceList.Contains(text, StringComparer.OrdinalIgnoreCase))
                    return $"Valoarea „{text}” nu se află printre opțiunile câmpului „{Label}”.";
                break;
        }

        return null;
    }

    /// <summary>Verifică definiția însăși, înainte de salvare.</summary>
    public IReadOnlyList<string> ValidateDefinition()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Label))
            errors.Add("Eticheta câmpului este obligatorie.");

        if (string.IsNullOrWhiteSpace(Key))
            errors.Add("Numele scurt este obligatoriu.");
        else if (!Key.All(c => char.IsLetterOrDigit(c) || c == '_'))
            errors.Add("Numele scurt poate conține doar litere, cifre și liniuță de subliniere.");

        if (Kind == CustomFieldKind.Choice && ChoiceList.Count == 0)
            errors.Add("Un câmp de tip listă are nevoie de cel puțin o opțiune.");

        return errors;
    }

    /// <summary>Transformă o etichetă în nume scurt: „Mărime preferată” devine „marime_preferata”.</summary>
    public static string SuggestKey(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        var simplified = label.ToLowerInvariant()
            .Replace("ă", "a").Replace("â", "a").Replace("î", "i")
            .Replace("ș", "s").Replace("ş", "s")
            .Replace("ț", "t").Replace("ţ", "t");

        var chars = simplified.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var key = new string(chars);

        while (key.Contains("__"))
            key = key.Replace("__", "_");

        return key.Trim('_');
    }
}
