using Fidelizare.Core.Model;

namespace Fidelizare.Core.Abstractions;

/// <summary>Criterii pentru lista de clienți din interfață.</summary>
public sealed class CustomerQuery
{
    /// <summary>Text căutat în nume, telefon, e-mail sau cod de card.</summary>
    public string? SearchText { get; init; }

    public bool IncludeInactive { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 200;
}

public interface ICustomerRepository
{
    Customer? GetById(int id);

    Customer? GetByCardCode(string cardCode);

    /// <summary>Caută după telefon normalizat (doar cifre).</summary>
    Customer? GetByPhone(string normalizedPhone);

    IReadOnlyList<Customer> Search(CustomerQuery query);

    int Count(CustomerQuery query);

    int Insert(Customer customer);

    void Update(Customer customer);

    /// <summary>Verifică unicitatea codului de card, ignorând clientul indicat.</summary>
    bool CardCodeExists(string cardCode, int? exceptCustomerId = null);

    bool PhoneExists(string normalizedPhone, int? exceptCustomerId = null);

    /// <summary>Cel mai mare cod numeric de card existent, pentru generarea următorului.</summary>
    long GetMaxNumericCardCode();

    /// <summary>Clienții care își serbează ziua de naștere în intervalul dat, indiferent de an.</summary>
    IReadOnlyList<Customer> GetBirthdaysBetween(DateTime fromInclusive, DateTime toInclusive);
}

public interface IPurchaseRepository
{
    Purchase? GetById(int id);

    IReadOnlyList<Purchase> GetForCustomer(int customerId, int take = 100);

    int Insert(Purchase purchase);

    void Update(Purchase purchase);

    /// <summary>Rulajul net al clientului începând cu data dată, fără cumpărările anulate.</summary>
    decimal GetNetSpendSince(int customerId, DateTime sinceInclusive);

    decimal GetLifetimeSpend(int customerId);

    int GetPurchaseCount(int customerId);

    DateTime? GetLastPurchaseDate(int customerId);

    /// <summary>Câte cumpărături neanulate a făcut clientul de la data dată.</summary>
    int GetPurchaseCountSince(int customerId, DateTime sinceInclusive);

    bool ReceiptNumberExists(string receiptNumber);

    IReadOnlyList<Purchase> GetBetween(DateTime fromInclusive, DateTime toExclusive);
}

public interface IPointsRepository
{
    /// <summary>Soldul disponibil: suma punctelor rămase din loturile neexpirate.</summary>
    int GetBalance(int customerId);

    /// <summary>Loturile cu puncte disponibile, în ordinea în care trebuie consumate.</summary>
    IReadOnlyList<PointLot> GetOpenLots(int customerId);

    int InsertLot(PointLot lot);

    void UpdateLot(PointLot lot);

    /// <summary>Loturile expirate care încă au puncte, pentru procesul de expirare.</summary>
    IReadOnlyList<PointLot> GetLotsToExpire(DateTime asOf, int? customerId = null);

    /// <summary>Punctele care expiră până la data dată.</summary>
    int GetPointsExpiringBefore(int customerId, DateTime beforeExclusive);

    DateTime? GetNextExpiryDate(int customerId);

    /// <summary>
    /// Punctele câștigate de la data dată: cumpărări, bonusuri și corecții în plus,
    /// din care se scad retragerile pentru vânzări anulate. Punctele cheltuite nu
    /// contează — altfel un client ar coborî din nivel tocmai fiindcă și-a folosit punctele.
    /// </summary>
    int GetPointsEarnedSince(int customerId, DateTime sinceInclusive);

    IReadOnlyList<PointLot> GetLotsForPurchase(int purchaseId);

    int InsertTransaction(PointTransaction transaction);

    IReadOnlyList<PointTransaction> GetTransactions(int customerId, int take = 200);

    IReadOnlyList<PointTransaction> GetTransactionsBetween(DateTime fromInclusive, DateTime toExclusive);

    PointLot? GetLotById(int lotId);

    /// <summary>Înregistrează din ce lot s-au luat punctele unei mișcări de consum.</summary>
    void InsertConsumption(PointLotConsumption consumption);

    /// <summary>Detaliul pe loturi al unei mișcări de consum, folosit la anulări.</summary>
    IReadOnlyList<PointLotConsumption> GetConsumptionsForTransaction(int transactionId);
}

public interface IRewardRepository
{
    Reward? GetById(int id);

    /// <summary>Toate recompensele, inclusiv cele inactive, pentru administrare.</summary>
    IReadOnlyList<Reward> GetAll();

    int Insert(Reward reward);

    void Update(Reward reward);

    void Delete(int id);
}

public interface IRedemptionRepository
{
    Redemption? GetById(int id);

    IReadOnlyList<Redemption> GetForCustomer(int customerId, int take = 100);

    int Insert(Redemption redemption);

    void Update(Redemption redemption);

    IReadOnlyList<Redemption> GetBetween(DateTime fromInclusive, DateTime toExclusive);
}

public interface ITierRepository
{
    /// <summary>Nivelurile în ordine crescătoare a pragului.</summary>
    IReadOnlyList<Tier> GetAll();

    Tier? GetById(int id);

    int Insert(Tier tier);

    void Update(Tier tier);

    void Delete(int id);
}

public interface ISettingsRepository
{
    string? Get(string key);

    IReadOnlyDictionary<string, string> GetAll();

    void Set(string key, string value);
}

public interface IUserRepository
{
    AppUser? GetById(int id);

    AppUser? GetByUsername(string username);

    IReadOnlyList<AppUser> GetAll();

    int Insert(AppUser user);

    void Update(AppUser user);

    bool UsernameExists(string username, int? exceptUserId = null);

    int CountActiveAdmins(int? exceptUserId = null);
}

public interface IAuditRepository
{
    int Insert(AuditEntry entry);

    IReadOnlyList<AuditEntry> GetRecent(int take = 500);

    IReadOnlyList<AuditEntry> GetBetween(DateTime fromInclusive, DateTime toExclusive);
}

public interface ICampaignRepository
{
    IReadOnlyList<Campaign> GetAll();

    Campaign? GetById(int id);

    int Insert(Campaign campaign);

    void Update(Campaign campaign);

    void Delete(int id);

    /// <summary>Campaniile active a căror perioadă include ziua dată.</summary>
    IReadOnlyList<Campaign> GetRunningOn(DateTime day);
}

public interface ICustomFieldRepository
{
    /// <summary>Definițiile câmpurilor, în ordinea de afișare.</summary>
    IReadOnlyList<CustomFieldDefinition> GetDefinitions(bool includeInactive = false);

    CustomFieldDefinition? GetDefinition(int id);

    int InsertDefinition(CustomFieldDefinition definition);

    void UpdateDefinition(CustomFieldDefinition definition);

    /// <summary>Șterge definiția împreună cu toate valorile completate pentru ea.</summary>
    void DeleteDefinition(int id);

    bool KeyExists(string key, int? exceptId = null);

    /// <summary>Valorile completate pentru un client, pe identificatorul definiției.</summary>
    IReadOnlyDictionary<int, string?> GetValues(int customerId);

    /// <summary>Înlocuiește valorile clientului. Cele goale se șterg, ca să nu ocupe spațiu.</summary>
    void SetValues(int customerId, IReadOnlyDictionary<int, string?> values);
}
