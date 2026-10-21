namespace Fidelizare.Core.Abstractions;

/// <summary>
/// O unitate de lucru: o conexiune la baza de date și o tranzacție care le
/// cuprinde pe toate operațiile din interior.
///
/// Acordarea de puncte atinge mai multe tabele deodată (cumpărare, lot de puncte,
/// registru). Dacă s-ar scrie separat, o întrerupere la mijloc ar lăsa clientul cu
/// o cumpărare fără puncte sau invers. Aici ori intră tot, ori nimic.
///
/// Tot acest strat este și punctul în care programul poate trece de la un singur
/// calculator la mai multe: implementarea se schimbă, restul aplicației nu.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    ICustomerRepository Customers { get; }
    IPurchaseRepository Purchases { get; }
    IPointsRepository Points { get; }
    IRewardRepository Rewards { get; }
    IRedemptionRepository Redemptions { get; }
    ITierRepository Tiers { get; }
    ICampaignRepository Campaigns { get; }
    ICustomFieldRepository CustomFields { get; }
    ISettingsRepository Settings { get; }
    IUserRepository Users { get; }
    IAuditRepository Audit { get; }

    /// <summary>
    /// Confirmă modificările. Dacă nu este apelat înainte de Dispose,
    /// tranzacția se anulează — ceea ce face ca ieșirea pe o excepție să fie sigură.
    /// </summary>
    void Commit();
}

/// <summary>Deschide unități de lucru. Injectat în servicii, ca acestea să nu știe de SQLite.</summary>
public interface IUnitOfWorkFactory
{
    IUnitOfWork Create();
}
