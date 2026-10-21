using System.Globalization;
using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;

namespace Fidelizare.Core.Services;

/// <summary>
/// Regulile programului de fidelizare, așa cum le vede magazinul. Sunt păstrate
/// ca perechi cheie/valoare în baza de date și pot fi schimbate din aplicație,
/// fără recompilare — magazinul își poate ajusta pragurile după sezon.
///
/// Valorile implicite de mai jos descriu un program clasic pentru un magazin de
/// haine: 10 lei cheltuiți = 1 punct, un punct valorează 0,10 lei la revendicare
/// (adică o rată de întoarcere de 1% pe nivelul de bază), praguri de nivel la
/// 1.000 și 3.000 lei rulaj anual, iar punctele expiră după 12 luni.
/// </summary>
public sealed class LoyaltyRules
{
    // Numele cheilor sunt stabile: odată scrise în baza de date, nu se mai schimbă.
    public const string KeyLeiPerPoint = "earn.lei_per_point";
    public const string KeyMinPurchaseForPoints = "earn.min_purchase";
    public const string KeyPointValueLei = "redeem.point_value_lei";
    public const string KeyMinimumRedeemablePoints = "redeem.minimum_points";
    public const string KeySelfVoidMinutes = "redeem.self_void_minutes";
    public const string KeyPointsValidityMonths = "expiry.validity_months";
    public const string KeyExpiryWarningDays = "expiry.warning_days";
    public const string KeyWelcomeBonusPoints = "bonus.welcome_points";
    public const string KeyBirthdayBonusPoints = "bonus.birthday_points";
    public const string KeyBirthdayWindowDays = "bonus.birthday_window_days";
    public const string KeyQualifyingPeriodMonths = "tier.qualifying_months";
    public const string KeyTierBasis = "tier.basis";
    public const string KeyCardPrefix = "card.prefix";
    public const string KeyCardDigits = "card.digits";
    public const string KeyAccentColor = "brand.accent";
    public const string KeyStoreName = "store.name";
    public const string KeyProgramName = "store.program_name";
    public const string KeySchemaVersion = "system.schema_version";
    public const string KeyLastExpiryRun = "system.last_expiry_run";

    /// <summary>Câți lei trebuie cheltuiți pentru un punct. 10 lei = 1 punct.</summary>
    public decimal LeiPerPoint { get; set; } = 10m;

    /// <summary>Sub această valoare a bonului nu se acordă puncte. 0 = fără prag.</summary>
    public decimal MinPurchaseForPoints { get; set; } = 0m;

    /// <summary>Cât valorează un punct în lei la revendicare. Folosit informativ și la recompensele generate automat.</summary>
    public decimal PointValueLei { get; set; } = 0.10m;

    /// <summary>Sub acest sold nu se poate revendica nimic, ca să nu se fragmenteze beneficiul.</summary>
    public int MinimumRedeemablePoints { get; set; } = 100;

    /// <summary>
    /// Cate minute are casierul la dispozitie ca sa isi anuleze singur o recompensa
    /// tocmai acordata, fara sa cheme responsabilul.
    ///
    /// Exista pentru greseala de acum zece secunde: recompensa gresita aleasa din
    /// lista, cu clientul inca la casa. Dincolo de acest interval anularea ramane
    /// o operatiune de responsabil, fiindca nu mai este o corectura pe loc, ci o
    /// modificare a unui istoric inchis. Zero dezactiveaza complet posibilitatea.
    /// </summary>
    public int SelfVoidMinutes { get; set; } = 10;

    /// <summary>După câte luni expiră un lot de puncte. 0 înseamnă că punctele nu expiră.</summary>
    public int PointsValidityMonths { get; set; } = 12;

    /// <summary>Cu câte zile înainte de expirare clientul este avertizat la casă.</summary>
    public int ExpiryWarningDays { get; set; } = 30;

    /// <summary>Puncte acordate la înscrierea în program. 0 = dezactivat.</summary>
    public int WelcomeBonusPoints { get; set; } = 50;

    /// <summary>Puncte acordate o dată pe an, în jurul zilei de naștere. 0 = dezactivat.</summary>
    public int BirthdayBonusPoints { get; set; } = 100;

    /// <summary>Câte zile înainte și după ziua de naștere mai poate fi acordat bonusul.</summary>
    public int BirthdayWindowDays { get; set; } = 7;

    /// <summary>
    /// Fereastra mobilă, în luni, pe care se calculează rulajul pentru nivel.
    /// 0 înseamnă că se ia în calcul tot istoricul, iar nivelul nu mai scade niciodată.
    /// </summary>
    public int QualifyingPeriodMonths { get; set; } = 12;

    /// <summary>
    /// Mărimea după care se stabilesc nivelurile. Schimbarea ei schimbă înțelesul
    /// pragurilor definite pe niveluri, deci după modificare pragurile trebuie recitite.
    /// </summary>
    public TierBasis TierBasis { get; set; } = TierBasis.Spend;

    public string StoreName { get; set; } = "Magazinul meu";

    public string ProgramName { get; set; } = "Club Fidelitate";

    /// <summary>Text pus în fața codurilor de card generate automat. Poate fi gol.</summary>
    public string CardCodePrefix { get; set; } = string.Empty;

    /// <summary>Câte cifre are partea numerică a codului de card.</summary>
    public int CardCodeDigits { get; set; } = 6;

    /// <summary>Culoarea de accent a interfeței, în format #RRGGBB.</summary>
    public string AccentColorHex { get; set; } = "#3B5BDB";

    /// <summary>Data de la care se numără rulajul pentru nivel, raportat la momentul dat.</summary>
    public DateTime GetQualifyingPeriodStart(DateTime asOf) =>
        QualifyingPeriodMonths <= 0 ? DateTime.MinValue : asOf.Date.AddMonths(-QualifyingPeriodMonths);

    /// <summary>Data expirării pentru puncte acordate acum. Null dacă expirarea este oprită.</summary>
    public DateTime? GetExpiryDate(DateTime earnedAt) =>
        PointsValidityMonths <= 0 ? null : earnedAt.Date.AddMonths(PointsValidityMonths);

    public static LoyaltyRules Load(ISettingsRepository settings)
    {
        var all = settings.GetAll();
        var rules = new LoyaltyRules();

        rules.LeiPerPoint = Dec(all, KeyLeiPerPoint, rules.LeiPerPoint);
        rules.MinPurchaseForPoints = Dec(all, KeyMinPurchaseForPoints, rules.MinPurchaseForPoints);
        rules.PointValueLei = Dec(all, KeyPointValueLei, rules.PointValueLei);
        rules.MinimumRedeemablePoints = Int(all, KeyMinimumRedeemablePoints, rules.MinimumRedeemablePoints);
        rules.SelfVoidMinutes = Int(all, KeySelfVoidMinutes, rules.SelfVoidMinutes);
        rules.PointsValidityMonths = Int(all, KeyPointsValidityMonths, rules.PointsValidityMonths);
        rules.ExpiryWarningDays = Int(all, KeyExpiryWarningDays, rules.ExpiryWarningDays);
        rules.WelcomeBonusPoints = Int(all, KeyWelcomeBonusPoints, rules.WelcomeBonusPoints);
        rules.BirthdayBonusPoints = Int(all, KeyBirthdayBonusPoints, rules.BirthdayBonusPoints);
        rules.BirthdayWindowDays = Int(all, KeyBirthdayWindowDays, rules.BirthdayWindowDays);
        rules.QualifyingPeriodMonths = Int(all, KeyQualifyingPeriodMonths, rules.QualifyingPeriodMonths);
        rules.TierBasis = (TierBasis)Int(all, KeyTierBasis, (int)rules.TierBasis);
        rules.CardCodePrefix = Str(all, KeyCardPrefix, rules.CardCodePrefix);
        rules.CardCodeDigits = Int(all, KeyCardDigits, rules.CardCodeDigits);
        rules.AccentColorHex = Str(all, KeyAccentColor, rules.AccentColorHex);
        rules.StoreName = Str(all, KeyStoreName, rules.StoreName);
        rules.ProgramName = Str(all, KeyProgramName, rules.ProgramName);

        return rules;
    }

    public void Save(ISettingsRepository settings)
    {
        // Valorile numerice se scriu în cultura invariantă, ca fișierul să fie
        // citit la fel indiferent de setările regionale ale calculatorului.
        settings.Set(KeyLeiPerPoint, LeiPerPoint.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyMinPurchaseForPoints, MinPurchaseForPoints.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyPointValueLei, PointValueLei.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyMinimumRedeemablePoints, MinimumRedeemablePoints.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeySelfVoidMinutes, SelfVoidMinutes.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyPointsValidityMonths, PointsValidityMonths.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyExpiryWarningDays, ExpiryWarningDays.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyWelcomeBonusPoints, WelcomeBonusPoints.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyBirthdayBonusPoints, BirthdayBonusPoints.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyBirthdayWindowDays, BirthdayWindowDays.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyQualifyingPeriodMonths, QualifyingPeriodMonths.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyTierBasis, ((int)TierBasis).ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyCardPrefix, CardCodePrefix);
        settings.Set(KeyCardDigits, CardCodeDigits.ToString(CultureInfo.InvariantCulture));
        settings.Set(KeyAccentColor, AccentColorHex);
        settings.Set(KeyStoreName, StoreName);
        settings.Set(KeyProgramName, ProgramName);
    }

    /// <summary>Verifică coerența valorilor înainte de salvare.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (LeiPerPoint <= 0)
            errors.Add("Valoarea „lei pentru un punct” trebuie să fie mai mare decât zero.");
        if (LeiPerPoint > 10_000)
            errors.Add("Valoarea „lei pentru un punct” este nerealist de mare.");
        if (MinPurchaseForPoints < 0)
            errors.Add("Pragul minim de cumpărare nu poate fi negativ.");
        if (PointValueLei <= 0)
            errors.Add("Valoarea unui punct trebuie să fie mai mare decât zero.");
        if (MinimumRedeemablePoints < 0)
            errors.Add("Pragul minim de revendicare nu poate fi negativ.");
        if (SelfVoidMinutes is < 0 or > 120)
            errors.Add("Intervalul de anulare de catre casier trebuie sa fie intre 0 si 120 de minute.");
        if (PointsValidityMonths < 0 || PointsValidityMonths > 240)
            errors.Add("Valabilitatea punctelor trebuie să fie între 0 și 240 de luni.");
        if (ExpiryWarningDays < 0 || ExpiryWarningDays > 365)
            errors.Add("Avertizarea de expirare trebuie să fie între 0 și 365 de zile.");
        if (WelcomeBonusPoints < 0)
            errors.Add("Bonusul de înscriere nu poate fi negativ.");
        if (BirthdayBonusPoints < 0)
            errors.Add("Bonusul de zi de naștere nu poate fi negativ.");
        if (BirthdayWindowDays < 0 || BirthdayWindowDays > 90)
            errors.Add("Fereastra pentru bonusul de zi de naștere trebuie să fie între 0 și 90 de zile.");
        if (QualifyingPeriodMonths < 0 || QualifyingPeriodMonths > 240)
            errors.Add("Perioada de calificare trebuie să fie între 0 și 240 de luni.");
        if (string.IsNullOrWhiteSpace(StoreName))
            errors.Add("Numele magazinului nu poate fi gol.");
        if (string.IsNullOrWhiteSpace(ProgramName))
            errors.Add("Numele programului nu poate fi gol.");
        if (CardCodeDigits is < 3 or > 12)
            errors.Add("Codul de card trebuie să aibă între 3 și 12 cifre.");
        if (CardCodePrefix.Length > 8)
            errors.Add("Prefixul codului de card poate avea cel mult 8 caractere.");
        if (CardCodePrefix.Any(c => !char.IsLetterOrDigit(c) && c != Convert.ToChar(45)))
            errors.Add("Prefixul codului de card poate conține doar litere, cifre și liniuță.");
        if (!IsValidHexColor(AccentColorHex))
            errors.Add("Culoarea de accent trebuie scrisă în forma #RRGGBB.");

        return errors;
    }

    public LoyaltyRules Clone() => (LoyaltyRules)MemberwiseClone();

    /// <summary>Verifică forma #RRGGBB, singura acceptată pentru culori.</summary>
    public static bool IsValidHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 7 &&
        value[0] == Convert.ToChar(35) &&
        value.Skip(1).All(Uri.IsHexDigit);

    private static decimal Dec(IReadOnlyDictionary<string, string> all, string key, decimal fallback) =>
        all.TryGetValue(key, out var raw) &&
        decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int Int(IReadOnlyDictionary<string, string> all, string key, int fallback) =>
        all.TryGetValue(key, out var raw) &&
        int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static string Str(IReadOnlyDictionary<string, string> all, string key, string fallback) =>
        all.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw : fallback;
}

/// <summary>Metode ajutătoare care depind de regulile alese de magazin.</summary>
public static class LoyaltyRulesExtensions
{
    /// <summary>Unitatea în care se exprimă pragurile de nivel, după baza aleasă.</summary>
    public static string QualifyingUnit(this LoyaltyRules rules) => rules.TierBasis switch
    {
        TierBasis.PointsEarned => "puncte",
        TierBasis.PurchaseCount => "cumpărări",
        _ => "lei",
    };

    /// <summary>Explicația bazei de calcul, afișată în ecranul de administrare.</summary>
    public static string QualifyingDescription(this LoyaltyRules rules) => rules.TierBasis switch
    {
        TierBasis.PointsEarned => "Nivelul se stabilește după punctele acumulate în perioada de calificare.",
        TierBasis.PurchaseCount => "Nivelul se stabilește după numărul de cumpărături din perioada de calificare.",
        _ => "Nivelul se stabilește după rulajul net, în lei, din perioada de calificare.",
    };

    /// <summary>
    /// Scrie o valoare de calificare împreună cu unitatea ei. Sumele primesc două
    /// zecimale, punctele și cumpărăturile niciuna — un „12,00 cumpărări” ar fi absurd.
    /// </summary>
    public static string FormatQualifyingValue(this LoyaltyRules rules, decimal value) =>
        rules.TierBasis == TierBasis.Spend
            ? $"{value:N2} lei"
            : $"{value:N0} {rules.QualifyingUnit()}";
}
