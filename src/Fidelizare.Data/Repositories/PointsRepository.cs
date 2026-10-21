using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Repositories;

internal sealed class PointsRepository : RepositoryBase, IPointsRepository
{
    private const string LotColumns =
        "Id, CustomerId, Source, PurchaseId, PointsOriginal, PointsRemaining, EarnedAt, ExpiresAt, IsExpired";

    private const string TransactionColumns =
        "Id, CustomerId, Type, Points, BalanceAfter, PointLotId, PurchaseId, RedemptionId, " +
        "OccurredAt, UserId, UserName, Note";

    public PointsRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public int GetBalance(int customerId)
    {
        // Soldul se calculează din loturi, nu dintr-un câmp întreținut separat.
        // Un câmp separat s-ar putea desincroniza; suma loturilor nu poate.
        using var command = Command("""
            SELECT IFNULL(SUM(PointsRemaining), 0)
            FROM PointLots
            WHERE CustomerId = @customer AND IsExpired = 0;
            """);

        command.With("@customer", customerId);
        return ScalarInt(command);
    }

    public IReadOnlyList<PointLot> GetOpenLots(int customerId)
    {
        // Ordinea contează: se consumă întâi punctele care expiră cel mai devreme,
        // ca ele să fie folosite înainte de a se pierde. Loturile fără expirare
        // vin la final, tocmai pentru că nu sunt în pericol.
        using var command = Command($"""
            SELECT {LotColumns}
            FROM PointLots
            WHERE CustomerId = @customer AND IsExpired = 0 AND PointsRemaining > 0
            ORDER BY
                CASE WHEN ExpiresAt IS NULL THEN 1 ELSE 0 END,
                ExpiresAt,
                EarnedAt,
                Id;
            """);

        command.With("@customer", customerId);
        return ReadLots(command);
    }

    public int InsertLot(PointLot lot)
    {
        using var command = Command("""
            INSERT INTO PointLots
                (CustomerId, Source, PurchaseId, PointsOriginal, PointsRemaining, EarnedAt, ExpiresAt, IsExpired)
            VALUES
                (@customer, @source, @purchase, @original, @remaining, @earned, @expires, @expired)
            RETURNING Id;
            """);

        BindLot(command, lot);
        return ExecuteInsert(command);
    }

    public void UpdateLot(PointLot lot)
    {
        using var command = Command("""
            UPDATE PointLots SET
                CustomerId      = @customer,
                Source          = @source,
                PurchaseId      = @purchase,
                PointsOriginal  = @original,
                PointsRemaining = @remaining,
                EarnedAt        = @earned,
                ExpiresAt       = @expires,
                IsExpired       = @expired
            WHERE Id = @id;
            """);

        BindLot(command, lot);
        command.With("@id", lot.Id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PointLot> GetLotsToExpire(DateTime asOf, int? customerId = null)
    {
        using var command = Command($"""
            SELECT {LotColumns}
            FROM PointLots
            WHERE IsExpired = 0
              AND ExpiresAt IS NOT NULL
              AND ExpiresAt <= @asOf
              AND (@customer IS NULL OR CustomerId = @customer)
            ORDER BY ExpiresAt, Id;
            """);

        command.With("@asOf", Db.ToText(asOf));
        command.With("@customer", customerId);

        return ReadLots(command);
    }

    public int GetPointsExpiringBefore(int customerId, DateTime beforeExclusive)
    {
        using var command = Command("""
            SELECT IFNULL(SUM(PointsRemaining), 0)
            FROM PointLots
            WHERE CustomerId = @customer
              AND IsExpired = 0
              AND PointsRemaining > 0
              AND ExpiresAt IS NOT NULL
              AND ExpiresAt < @before;
            """);

        command.With("@customer", customerId);
        command.With("@before", Db.ToText(beforeExclusive));

        return ScalarInt(command);
    }

    public DateTime? GetNextExpiryDate(int customerId)
    {
        using var command = Command("""
            SELECT MIN(ExpiresAt)
            FROM PointLots
            WHERE CustomerId = @customer AND IsExpired = 0 AND PointsRemaining > 0 AND ExpiresAt IS NOT NULL;
            """);

        command.With("@customer", customerId);

        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Db.ParseTimestamp(Convert.ToString(value)!);
    }

    public int GetPointsEarnedSince(int customerId, DateTime sinceInclusive)
    {
        // Se numara doar miscarile care aduc puncte, plus retragerile pentru
        // vanzari anulate si retururi, care sunt negative si scad singure din total.
        // Consumul pe recompense si expirarea raman in afara: nivelul reflecta
        // cat a acumulat clientul, nu cat i-a mai ramas.
        using var command = Command("""
            SELECT IFNULL(SUM(Points), 0)
            FROM PointTransactions
            WHERE CustomerId = @customer
              AND OccurredAt >= @since
              AND Type IN (1, 4, 6, 8, 9, 10);
            """);

        command.With("@customer", customerId);
        command.With("@since", Db.ToText(sinceInclusive));

        return Math.Max(0, ScalarInt(command));
    }

    public IReadOnlyList<PointLot> GetLotsForPurchase(int purchaseId)
    {
        using var command = Command($"""
            SELECT {LotColumns}
            FROM PointLots
            WHERE PurchaseId = @purchase
            ORDER BY Id;
            """);

        command.With("@purchase", purchaseId);
        return ReadLots(command);
    }

    public PointLot? GetLotById(int lotId)
    {
        using var command = Command($"SELECT {LotColumns} FROM PointLots WHERE Id = @id;");
        command.With("@id", lotId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? MapLot(reader) : null;
    }

    public int InsertTransaction(PointTransaction transaction)
    {
        using var command = Command("""
            INSERT INTO PointTransactions
                (CustomerId, Type, Points, BalanceAfter, PointLotId, PurchaseId, RedemptionId,
                 OccurredAt, UserId, UserName, Note)
            VALUES
                (@customer, @type, @points, @balance, @lot, @purchase, @redemption,
                 @occurred, @userId, @userName, @note)
            RETURNING Id;
            """);

        command.With("@customer", transaction.CustomerId);
        command.With("@type", (int)transaction.Type);
        command.With("@points", transaction.Points);
        command.With("@balance", transaction.BalanceAfter);
        command.With("@lot", transaction.PointLotId);
        command.With("@purchase", transaction.PurchaseId);
        command.With("@redemption", transaction.RedemptionId);
        command.With("@occurred", Db.ToText(transaction.OccurredAt));
        command.With("@userId", transaction.UserId);
        command.With("@userName", transaction.UserName);
        command.With("@note", transaction.Note);

        return ExecuteInsert(command);
    }

    public IReadOnlyList<PointTransaction> GetTransactions(int customerId, int take = 200)
    {
        using var command = Command($"""
            SELECT {TransactionColumns}
            FROM PointTransactions
            WHERE CustomerId = @customer
            ORDER BY OccurredAt DESC, Id DESC
            LIMIT @take;
            """);

        command.With("@customer", customerId);
        command.With("@take", take);

        return ReadTransactions(command);
    }

    public IReadOnlyList<PointTransaction> GetTransactionsBetween(DateTime fromInclusive, DateTime toExclusive)
    {
        using var command = Command($"""
            SELECT {TransactionColumns}
            FROM PointTransactions
            WHERE OccurredAt >= @from AND OccurredAt < @to
            ORDER BY OccurredAt, Id;
            """);

        command.With("@from", Db.ToText(fromInclusive));
        command.With("@to", Db.ToText(toExclusive));

        return ReadTransactions(command);
    }

    public void InsertConsumption(PointLotConsumption consumption)
    {
        using var command = Command("""
            INSERT INTO PointLotConsumptions (PointLotId, PointTransactionId, Points, OccurredAt)
            VALUES (@lot, @transaction, @points, @occurred);
            """);

        command.With("@lot", consumption.PointLotId);
        command.With("@transaction", consumption.PointTransactionId);
        command.With("@points", consumption.Points);
        command.With("@occurred", Db.ToText(consumption.OccurredAt));

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PointLotConsumption> GetConsumptionsForTransaction(int transactionId)
    {
        using var command = Command("""
            SELECT Id, PointLotId, PointTransactionId, Points, OccurredAt
            FROM PointLotConsumptions
            WHERE PointTransactionId = @transaction
            ORDER BY Id;
            """);

        command.With("@transaction", transactionId);

        var results = new List<PointLotConsumption>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new PointLotConsumption
            {
                Id = reader.GetInt("Id"),
                PointLotId = reader.GetInt("PointLotId"),
                PointTransactionId = reader.GetInt("PointTransactionId"),
                Points = reader.GetInt("Points"),
                OccurredAt = reader.GetTimestamp("OccurredAt"),
            });
        }

        return results;
    }

    private static void BindLot(SqliteCommand command, PointLot lot)
    {
        command.With("@customer", lot.CustomerId);
        command.With("@source", (int)lot.Source);
        command.With("@purchase", lot.PurchaseId);
        command.With("@original", lot.PointsOriginal);
        command.With("@remaining", lot.PointsRemaining);
        command.With("@earned", Db.ToText(lot.EarnedAt));
        command.With("@expires", Db.ToTextOrNull(lot.ExpiresAt));
        command.With("@expired", lot.IsExpired ? 1 : 0);
    }

    private static List<PointLot> ReadLots(SqliteCommand command)
    {
        var results = new List<PointLot>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(MapLot(reader));

        return results;
    }

    private static PointLot MapLot(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt("Id"),
        CustomerId = reader.GetInt("CustomerId"),
        Source = (PointLotSource)reader.GetInt("Source"),
        PurchaseId = reader.GetIntOrNull("PurchaseId"),
        PointsOriginal = reader.GetInt("PointsOriginal"),
        PointsRemaining = reader.GetInt("PointsRemaining"),
        EarnedAt = reader.GetTimestamp("EarnedAt"),
        ExpiresAt = reader.GetTimestampOrNull("ExpiresAt"),
        IsExpired = reader.GetBool("IsExpired"),
    };

    private static List<PointTransaction> ReadTransactions(SqliteCommand command)
    {
        var results = new List<PointTransaction>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new PointTransaction
            {
                Id = reader.GetInt("Id"),
                CustomerId = reader.GetInt("CustomerId"),
                Type = (PointTransactionType)reader.GetInt("Type"),
                Points = reader.GetInt("Points"),
                BalanceAfter = reader.GetInt("BalanceAfter"),
                PointLotId = reader.GetIntOrNull("PointLotId"),
                PurchaseId = reader.GetIntOrNull("PurchaseId"),
                RedemptionId = reader.GetIntOrNull("RedemptionId"),
                OccurredAt = reader.GetTimestamp("OccurredAt"),
                UserId = reader.GetIntOrNull("UserId"),
                UserName = reader.GetTextOrNull("UserName"),
                Note = reader.GetTextOrNull("Note"),
            });
        }

        return results;
    }
}
