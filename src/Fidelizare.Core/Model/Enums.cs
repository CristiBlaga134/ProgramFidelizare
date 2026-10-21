namespace Fidelizare.Core.Model;

/// <summary>
/// Tipul unei mișcări din registrul de puncte. Registrul este append-only:
/// nu se șterge și nu se modifică nimic, orice corecție este o mișcare nouă.
/// </summary>
public enum PointTransactionType
{
    /// <summary>Puncte acordate pentru o cumpărare.</summary>
    Earned = 1,

    /// <summary>Puncte consumate la revendicarea unei recompense.</summary>
    Redeemed = 2,

    /// <summary>Puncte pierdute prin expirare.</summary>
    Expired = 3,

    /// <summary>Corecție manuală în plus, făcută de un responsabil.</summary>
    AdjustmentPlus = 4,

    /// <summary>Corecție manuală în minus, făcută de un responsabil.</summary>
    AdjustmentMinus = 5,

    /// <summary>Anularea unei cumpărări: punctele acordate se retrag.</summary>
    ReversalOfEarning = 6,

    /// <summary>Anularea unei recompense: punctele consumate se restituie.</summary>
    ReversalOfRedemption = 7,

    /// <summary>Bonus de înscriere în program.</summary>
    Welcome = 8,

    /// <summary>Bonus acordat de ziua de naștere.</summary>
    Birthday = 9,

    /// <summary>Retur de produse: se retrag punctele aferente sumei returnate.</summary>
    Return = 10,
}

/// <summary>Motivul pentru care a fost creat un lot de puncte.</summary>
public enum PointLotSource
{
    Purchase = 1,
    Welcome = 2,
    Birthday = 3,
    ManualAdjustment = 4,
    Promotion = 5,
}

/// <summary>Felul beneficiului oferit de o recompensă.</summary>
public enum RewardKind
{
    /// <summary>Reducere fixă în lei (ex. 25 lei).</summary>
    FixedAmountDiscount = 1,

    /// <summary>Reducere procentuală din valoarea coșului (ex. 10%).</summary>
    PercentageDiscount = 2,

    /// <summary>Produs sau serviciu oferit gratuit (ex. o pereche de șosete).</summary>
    FreeProduct = 3,
}

/// <summary>Nivelul de acces al utilizatorului aplicației.</summary>
public enum UserRole
{
    /// <summary>Operează la casă: caută clienți, înregistrează vânzări, acordă recompense.</summary>
    Cashier = 1,

    /// <summary>În plus: corecții de puncte, anulări, rapoarte.</summary>
    Manager = 2,

    /// <summary>În plus: configurarea regulilor programului și gestiunea utilizatorilor.</summary>
    Admin = 3,
}

/// <summary>
/// Mărimea după care se stabilește nivelul unui client.
///
/// Programele de fidelizare nu folosesc toate aceeași bază: unele premiază banii
/// cheltuiți, altele punctele strânse, altele frecvența vizitelor. Alegerea
/// aparține magazinului, nu programului.
/// </summary>
public enum TierBasis
{
    /// <summary>Rulajul net în lei din perioada de calificare.</summary>
    Spend = 1,

    /// <summary>Punctele acumulate în perioada de calificare.</summary>
    PointsEarned = 2,

    /// <summary>Numărul de cumpărături din perioada de calificare.</summary>
    PurchaseCount = 3,
}

/// <summary>Felul unui câmp adăugat de magazin pe fișa clientului.</summary>
public enum CustomFieldKind
{
    /// <summary>Text liber: observații, stil preferat.</summary>
    Text = 1,

    /// <summary>Număr: mărime, număr de copii.</summary>
    Number = 2,

    /// <summary>Dată calendaristică.</summary>
    Date = 3,

    /// <summary>Bifă: da sau nu.</summary>
    YesNo = 4,

    /// <summary>Alegere dintr-o listă fixă, definită de magazin.</summary>
    Choice = 5,
}
