namespace Fidelizare.Core.Model;

/// <summary>Un client înscris în programul de fidelizare.</summary>
public sealed class Customer
{
    public int Id { get; set; }

    /// <summary>Codul tipărit pe cardul de fidelitate. Unic, folosit la căutarea rapidă la casă.</summary>
    public string CardCode { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Telefon normalizat (doar cifre). Unic dacă este completat; a doua cale de căutare la casă.</summary>
    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>Doar data, fără oră. Folosită pentru bonusul de zi de naștere.</summary>
    public DateTime? BirthDate { get; set; }

    public DateTime EnrolledAt { get; set; }

    /// <summary>Un client inactiv nu mai apare în căutări și nu mai acumulează puncte.</summary>
    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    /// <summary>Acord pentru comunicări comerciale. Punctele funcționează și fără el.</summary>
    public bool MarketingConsent { get; set; }

    /// <summary>Momentul exprimării acordului, păstrat ca dovadă.</summary>
    public DateTime? ConsentAt { get; set; }

    /// <summary>Ultima dată la care bonusul de zi de naștere a fost acordat, ca să nu se dubleze.</summary>
    public int? LastBirthdayBonusYear { get; set; }

    public string FullName => $"{LastName} {FirstName}".Trim();

    /// <summary>
    /// Valorile câmpurilor definite de magazin, pe identificatorul definiției.
    /// Se încarcă odată cu clientul, ca fișa să nu ceară o interogare per câmp.
    /// </summary>
    public Dictionary<int, string?> CustomValues { get; } = [];
}

/// <summary>
/// Clientul împreună cu valorile calculate de care are nevoie interfața:
/// sold de puncte, nivel, rulaj. Se citește într-o singură interogare.
/// </summary>
public sealed class CustomerSummary
{
    public required Customer Customer { get; init; }

    /// <summary>Punctele disponibile acum, din loturi neexpirate.</summary>
    public int PointsBalance { get; init; }

    /// <summary>Nivelul curent, determinat de rulajul din perioada de calificare.</summary>
    public required Tier Tier { get; init; }

    /// <summary>Următorul nivel, dacă mai există unul deasupra.</summary>
    public Tier? NextTier { get; init; }

    /// <summary>Rulaj net în perioada de calificare, pe baza căruia s-a stabilit nivelul.</summary>
    public decimal QualifyingValue { get; init; }

    /// <summary>Cât mai trebuie cheltuit până la nivelul următor. 0 dacă este la nivelul maxim.</summary>
    public decimal ValueToNextTier { get; init; }

    /// <summary>Totalul cheltuit de la înscriere, fără cumpărările anulate.</summary>
    public decimal LifetimeSpend { get; init; }

    public int PurchaseCount { get; init; }

    public DateTime? LastPurchaseAt { get; init; }

    /// <summary>Punctele care expiră în intervalul de avertizare configurat.</summary>
    public int PointsExpiringSoon { get; init; }

    /// <summary>Data la care expiră primul lot de puncte, dacă există puncte.</summary>
    public DateTime? NextExpiryAt { get; init; }
}
