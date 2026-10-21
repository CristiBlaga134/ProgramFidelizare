using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Repositories;

internal sealed class PurchaseRepository : RepositoryBase, IPurchaseRepository
{
    private const string Columns =
        "Id, CustomerId, ReceiptNumber, GrossAmountBani, DiscountAmountBani, NetAmountBani, " +
        "PointsEarned, TierAtPurchase, MultiplierMilli, OccurredAt, RecordedByUserId, " +
        "RecordedByUserName, IsVoided, VoidedAt, VoidReason, CampaignName, CampaignMultiplierMilli, " +
        "ReturnedAmountBani, PointsReturned";

    private const string ReturnColumns =
        "Id, PurchaseId, CustomerId, AmountBani, PointsReversed, PointsShortfall, Reason, OccurredAt, UserId, UserName";

    public PurchaseRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public Purchase? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM Purchases WHERE Id = @id;");
        command.With("@id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Purchase> GetForCustomer(int customerId, int take = 100)
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM Purchases
            WHERE CustomerId = @customer
            ORDER BY OccurredAt DESC, Id DESC
            LIMIT @take;
            """);

        command.With("@customer", customerId);
        command.With("@take", take);

        return ReadAll(command);
    }

    public int Insert(Purchase purchase)
    {
        using var command = Command("""
            INSERT INTO Purchases
                (CustomerId, ReceiptNumber, GrossAmountBani, DiscountAmountBani, NetAmountBani,
                 PointsEarned, TierAtPurchase, MultiplierMilli, OccurredAt, RecordedByUserId,
                 RecordedByUserName, IsVoided, VoidedAt, VoidReason, CampaignName, CampaignMultiplierMilli,
                 ReturnedAmountBani, PointsReturned)
            VALUES
                (@customer, @receipt, @gross, @discount, @net,
                 @points, @tier, @multiplier, @occurred, @userId,
                 @userName, @voided, @voidedAt, @voidReason, @campaign, @campaignMultiplier,
                 @returned, @pointsReturned)
            RETURNING Id;
            """);

        Bind(command, purchase);
        return ExecuteInsert(command);
    }

    public void Update(Purchase purchase)
    {
        using var command = Command("""
            UPDATE Purchases SET
                CustomerId         = @customer,
                ReceiptNumber      = @receipt,
                GrossAmountBani    = @gross,
                DiscountAmountBani = @discount,
                NetAmountBani      = @net,
                PointsEarned       = @points,
                TierAtPurchase     = @tier,
                MultiplierMilli    = @multiplier,
                OccurredAt         = @occurred,
                RecordedByUserId   = @userId,
                RecordedByUserName = @userName,
                IsVoided           = @voided,
                VoidedAt           = @voidedAt,
                VoidReason         = @voidReason,
                CampaignName       = @campaign,
                CampaignMultiplierMilli = @campaignMultiplier,
                ReturnedAmountBani = @returned,
                PointsReturned     = @pointsReturned
            WHERE Id = @id;
            """);

        Bind(command, purchase);
        command.With("@id", purchase.Id);
        command.ExecuteNonQuery();
    }

    public decimal GetNetSpendSince(int customerId, DateTime sinceInclusive)
    {
        // Cumpărările anulate și sumele returnate nu contează la calculul nivelului:
        // altfel un bon stornat ar ține clientul artificial pe un nivel superior.
        using var command = Command("""
            SELECT IFNULL(SUM(NetAmountBani - ReturnedAmountBani), 0)
            FROM Purchases
            WHERE CustomerId = @customer AND IsVoided = 0 AND OccurredAt >= @since;
            """);

        command.With("@customer", customerId);
        command.With("@since", Db.ToText(sinceInclusive == DateTime.MinValue ? DateTime.MinValue : sinceInclusive));

        return Db.FromBani(ScalarLong(command));
    }

    public decimal GetLifetimeSpend(int customerId)
    {
        using var command = Command("""
            SELECT IFNULL(SUM(NetAmountBani - ReturnedAmountBani), 0)
            FROM Purchases
            WHERE CustomerId = @customer AND IsVoided = 0;
            """);

        command.With("@customer", customerId);
        return Db.FromBani(ScalarLong(command));
    }

    public int GetPurchaseCount(int customerId)
    {
        using var command = Command(
            "SELECT COUNT(*) FROM Purchases WHERE CustomerId = @customer AND IsVoided = 0;");

        command.With("@customer", customerId);
        return ScalarInt(command);
    }

    public DateTime? GetLastPurchaseDate(int customerId)
    {
        using var command = Command(
            "SELECT MAX(OccurredAt) FROM Purchases WHERE CustomerId = @customer AND IsVoided = 0;");

        command.With("@customer", customerId);

        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Db.ParseTimestamp(Convert.ToString(value)!);
    }

    public int GetPurchaseCountSince(int customerId, DateTime sinceInclusive)
    {
        using var command = Command("""
            SELECT COUNT(*)
            FROM Purchases
            WHERE CustomerId = @customer AND IsVoided = 0 AND OccurredAt >= @since;
            """);

        command.With("@customer", customerId);
        command.With("@since", Db.ToText(sinceInclusive));

        return ScalarInt(command);
    }

    public bool ReceiptNumberExists(string receiptNumber)
    {
        // Un bon anulat poate fi reintrodus după corectare, deci verificarea
        // ignoră înregistrările anulate.
        using var command = Command(
            "SELECT COUNT(*) FROM Purchases WHERE ReceiptNumber = @receipt AND IsVoided = 0;");

        command.With("@receipt", receiptNumber);
        return ScalarInt(command) > 0;
    }

    public Purchase? GetByReceiptNumber(string receiptNumber)
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM Purchases
            WHERE ReceiptNumber = @receipt
            ORDER BY IsVoided, OccurredAt DESC, Id DESC
            LIMIT 1;
            """);

        command.With("@receipt", receiptNumber);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Purchase> GetBetween(DateTime fromInclusive, DateTime toExclusive)
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM Purchases
            WHERE OccurredAt >= @from AND OccurredAt < @to
            ORDER BY OccurredAt, Id;
            """);

        command.With("@from", Db.ToText(fromInclusive));
        command.With("@to", Db.ToText(toExclusive));

        return ReadAll(command);
    }

    public int InsertReturn(PurchaseReturn purchaseReturn)
    {
        using var command = Command("""
            INSERT INTO PurchaseReturns
                (PurchaseId, CustomerId, AmountBani, PointsReversed, PointsShortfall, Reason, OccurredAt, UserId, UserName)
            VALUES
                (@purchase, @customer, @amount, @points, @shortfall, @reason, @occurred, @userId, @userName)
            RETURNING Id;
            """);

        command.With("@purchase", purchaseReturn.PurchaseId);
        command.With("@customer", purchaseReturn.CustomerId);
        command.With("@amount", Db.ToBani(purchaseReturn.Amount));
        command.With("@points", purchaseReturn.PointsReversed);
        command.With("@shortfall", purchaseReturn.PointsShortfall);
        command.With("@reason", purchaseReturn.Reason);
        command.With("@occurred", Db.ToText(purchaseReturn.OccurredAt));
        command.With("@userId", purchaseReturn.UserId);
        command.With("@userName", purchaseReturn.UserName);

        return ExecuteInsert(command);
    }

    public IReadOnlyList<PurchaseReturn> GetReturnsForPurchase(int purchaseId)
    {
        using var command = Command($"""
            SELECT {ReturnColumns}
            FROM PurchaseReturns
            WHERE PurchaseId = @purchase
            ORDER BY OccurredAt, Id;
            """);

        command.With("@purchase", purchaseId);

        var results = new List<PurchaseReturn>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new PurchaseReturn
            {
                Id = reader.GetInt("Id"),
                PurchaseId = reader.GetInt("PurchaseId"),
                CustomerId = reader.GetInt("CustomerId"),
                Amount = reader.GetMoney("AmountBani"),
                PointsReversed = reader.GetInt("PointsReversed"),
                PointsShortfall = reader.GetInt("PointsShortfall"),
                Reason = reader.GetText("Reason"),
                OccurredAt = reader.GetTimestamp("OccurredAt"),
                UserId = reader.GetIntOrNull("UserId"),
                UserName = reader.GetTextOrNull("UserName"),
            });
        }

        return results;
    }

    private static List<Purchase> ReadAll(SqliteCommand command)
    {
        var results = new List<Purchase>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    private static void Bind(SqliteCommand command, Purchase purchase)
    {
        command.With("@customer", purchase.CustomerId);
        command.With("@receipt", purchase.ReceiptNumber);
        command.With("@gross", Db.ToBani(purchase.GrossAmount));
        command.With("@discount", Db.ToBani(purchase.DiscountAmount));
        command.With("@net", Db.ToBani(purchase.NetAmount));
        command.With("@points", purchase.PointsEarned);
        command.With("@tier", purchase.TierAtPurchase);
        command.With("@multiplier", Db.ToMilli(purchase.MultiplierApplied));
        command.With("@occurred", Db.ToText(purchase.OccurredAt));
        command.With("@userId", purchase.RecordedByUserId);
        command.With("@userName", purchase.RecordedByUserName);
        command.With("@voided", purchase.IsVoided ? 1 : 0);
        command.With("@voidedAt", Db.ToTextOrNull(purchase.VoidedAt));
        command.With("@voidReason", purchase.VoidReason);
        command.With("@campaign", purchase.CampaignName);
        command.With("@campaignMultiplier", Db.ToMilli(purchase.CampaignMultiplier));
        command.With("@returned", Db.ToBani(purchase.ReturnedAmount));
        command.With("@pointsReturned", purchase.PointsReturned);
    }

    private static Purchase Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt("Id"),
        CustomerId = reader.GetInt("CustomerId"),
        ReceiptNumber = reader.GetTextOrNull("ReceiptNumber"),
        GrossAmount = reader.GetMoney("GrossAmountBani"),
        DiscountAmount = reader.GetMoney("DiscountAmountBani"),
        NetAmount = reader.GetMoney("NetAmountBani"),
        PointsEarned = reader.GetInt("PointsEarned"),
        TierAtPurchase = reader.GetText("TierAtPurchase"),
        MultiplierApplied = reader.GetMilli("MultiplierMilli"),
        OccurredAt = reader.GetTimestamp("OccurredAt"),
        RecordedByUserId = reader.GetIntOrNull("RecordedByUserId"),
        RecordedByUserName = reader.GetTextOrNull("RecordedByUserName"),
        IsVoided = reader.GetBool("IsVoided"),
        VoidedAt = reader.GetTimestampOrNull("VoidedAt"),
        VoidReason = reader.GetTextOrNull("VoidReason"),
        CampaignName = reader.GetTextOrNull("CampaignName"),
        CampaignMultiplier = reader.GetMilli("CampaignMultiplierMilli"),
        ReturnedAmount = reader.GetMoney("ReturnedAmountBani"),
        PointsReturned = reader.GetInt("PointsReturned"),
    };
}
