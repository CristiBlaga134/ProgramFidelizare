using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Repositories;

internal sealed class TierRepository : RepositoryBase, ITierRepository
{
    private const string Columns =
        "Id, Name, ThresholdScaled, EarnMultiplierMilli, StandingDiscountMilli, ColorHex, SortOrder, Benefits";

    public TierRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public IReadOnlyList<Tier> GetAll()
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM Tiers
            ORDER BY ThresholdScaled, SortOrder, Id;
            """);

        var results = new List<Tier>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    public Tier? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM Tiers WHERE Id = @id;");
        command.With("@id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public int Insert(Tier tier)
    {
        using var command = Command("""
            INSERT INTO Tiers
                (Name, ThresholdScaled, EarnMultiplierMilli, StandingDiscountMilli, ColorHex, SortOrder, Benefits)
            VALUES
                (@name, @minSpend, @multiplier, @standing, @color, @sort, @benefits)
            RETURNING Id;
            """);

        Bind(command, tier);
        return ExecuteInsert(command);
    }

    public void Update(Tier tier)
    {
        using var command = Command("""
            UPDATE Tiers SET
                Name                   = @name,
                ThresholdScaled = @minSpend,
                EarnMultiplierMilli    = @multiplier,
                StandingDiscountMilli  = @standing,
                ColorHex               = @color,
                SortOrder              = @sort,
                Benefits               = @benefits
            WHERE Id = @id;
            """);

        Bind(command, tier);
        command.With("@id", tier.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        // Recompensele care cereau acest nivel rămân valabile, dar fără restricție,
        // ca ștergerea unui nivel să nu le scoată tăcut din catalog.
        using var detach = Command("UPDATE Rewards SET MinTierId = NULL WHERE MinTierId = @id;");
        detach.With("@id", id);
        detach.ExecuteNonQuery();

        using var command = Command("DELETE FROM Tiers WHERE Id = @id;");
        command.With("@id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, Tier tier)
    {
        command.With("@name", tier.Name);
        command.With("@minSpend", Db.ToBani(tier.Threshold));
        command.With("@multiplier", Db.ToMilli(tier.EarnMultiplier));
        command.With("@standing", Db.ToMilli(tier.StandingDiscountPercent));
        command.With("@color", tier.ColorHex);
        command.With("@sort", tier.SortOrder);
        command.With("@benefits", tier.Benefits);
    }

    private static Tier Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt("Id"),
        Name = reader.GetText("Name"),
        Threshold = reader.GetMoney("ThresholdScaled"),
        EarnMultiplier = reader.GetMilli("EarnMultiplierMilli"),
        StandingDiscountPercent = reader.GetMilli("StandingDiscountMilli"),
        ColorHex = reader.GetText("ColorHex"),
        SortOrder = reader.GetInt("SortOrder"),
        Benefits = reader.GetTextOrNull("Benefits"),
    };
}

internal sealed class RewardRepository : RepositoryBase, IRewardRepository
{
    private const string Columns =
        "Id, Name, Description, PointsCost, Kind, ValueScaled, MinPurchaseBani, MaxDiscountBani, " +
        "MinTierId, IsActive, ValidFrom, ValidTo, SortOrder";

    public RewardRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public Reward? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM Rewards WHERE Id = @id;");
        command.With("@id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Reward> GetAll()
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM Rewards
            ORDER BY SortOrder, PointsCost, Id;
            """);

        var results = new List<Reward>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    public int Insert(Reward reward)
    {
        using var command = Command("""
            INSERT INTO Rewards
                (Name, Description, PointsCost, Kind, ValueScaled, MinPurchaseBani, MaxDiscountBani,
                 MinTierId, IsActive, ValidFrom, ValidTo, SortOrder)
            VALUES
                (@name, @description, @cost, @kind, @value, @minPurchase, @maxDiscount,
                 @minTier, @active, @from, @to, @sort)
            RETURNING Id;
            """);

        Bind(command, reward);
        return ExecuteInsert(command);
    }

    public void Update(Reward reward)
    {
        using var command = Command("""
            UPDATE Rewards SET
                Name            = @name,
                Description     = @description,
                PointsCost      = @cost,
                Kind            = @kind,
                ValueScaled     = @value,
                MinPurchaseBani = @minPurchase,
                MaxDiscountBani = @maxDiscount,
                MinTierId       = @minTier,
                IsActive        = @active,
                ValidFrom       = @from,
                ValidTo         = @to,
                SortOrder       = @sort
            WHERE Id = @id;
            """);

        Bind(command, reward);
        command.With("@id", reward.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        // Recompensele deja acordate păstrează o copie a numelui și a valorii,
        // deci ștergerea din catalog nu strică istoricul clienților.
        using var command = Command("DELETE FROM Rewards WHERE Id = @id;");
        command.With("@id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, Reward reward)
    {
        command.With("@name", reward.Name);
        command.With("@description", reward.Description);
        command.With("@cost", reward.PointsCost);
        command.With("@kind", (int)reward.Kind);
        // Aceeași scalare cu doi zecimali servește și pentru lei, și pentru procente.
        command.With("@value", Db.ToBani(reward.Value));
        command.With("@minPurchase", Db.ToBani(reward.MinPurchaseAmount));
        command.With("@maxDiscount", Db.ToBani(reward.MaxDiscountAmount));
        command.With("@minTier", reward.MinTierId);
        command.With("@active", reward.IsActive ? 1 : 0);
        command.With("@from", Db.ToDateTextOrNull(reward.ValidFrom));
        command.With("@to", Db.ToDateTextOrNull(reward.ValidTo));
        command.With("@sort", reward.SortOrder);
    }

    private static Reward Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt("Id"),
        Name = reader.GetText("Name"),
        Description = reader.GetTextOrNull("Description"),
        PointsCost = reader.GetInt("PointsCost"),
        Kind = (RewardKind)reader.GetInt("Kind"),
        Value = reader.GetMoney("ValueScaled"),
        MinPurchaseAmount = reader.GetMoney("MinPurchaseBani"),
        MaxDiscountAmount = reader.GetMoney("MaxDiscountBani"),
        MinTierId = reader.GetIntOrNull("MinTierId"),
        IsActive = reader.GetBool("IsActive"),
        ValidFrom = reader.GetDateOrNull("ValidFrom"),
        ValidTo = reader.GetDateOrNull("ValidTo"),
        SortOrder = reader.GetInt("SortOrder"),
    };
}

internal sealed class RedemptionRepository : RepositoryBase, IRedemptionRepository
{
    private const string Columns =
        "Id, CustomerId, RewardId, RewardName, Kind, PointsSpent, DiscountValueBani, BasketAmountBani, " +
        "ReceiptNumber, PointTransactionId, OccurredAt, UserId, UserName, IsVoided, VoidedAt, VoidReason";

    public RedemptionRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public Redemption? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM Redemptions WHERE Id = @id;");
        command.With("@id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Redemption> GetForCustomer(int customerId, int take = 100)
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM Redemptions
            WHERE CustomerId = @customer
            ORDER BY OccurredAt DESC, Id DESC
            LIMIT @take;
            """);

        command.With("@customer", customerId);
        command.With("@take", take);

        return ReadAll(command);
    }

    public int Insert(Redemption redemption)
    {
        using var command = Command("""
            INSERT INTO Redemptions
                (CustomerId, RewardId, RewardName, Kind, PointsSpent, DiscountValueBani, BasketAmountBani,
                 ReceiptNumber, PointTransactionId, OccurredAt, UserId, UserName, IsVoided, VoidedAt, VoidReason)
            VALUES
                (@customer, @reward, @rewardName, @kind, @points, @discount, @basket,
                 @receipt, @transaction, @occurred, @userId, @userName, @voided, @voidedAt, @voidReason)
            RETURNING Id;
            """);

        Bind(command, redemption);
        return ExecuteInsert(command);
    }

    public void Update(Redemption redemption)
    {
        using var command = Command("""
            UPDATE Redemptions SET
                CustomerId         = @customer,
                RewardId           = @reward,
                RewardName         = @rewardName,
                Kind               = @kind,
                PointsSpent        = @points,
                DiscountValueBani  = @discount,
                BasketAmountBani   = @basket,
                ReceiptNumber      = @receipt,
                PointTransactionId = @transaction,
                OccurredAt         = @occurred,
                UserId             = @userId,
                UserName           = @userName,
                IsVoided           = @voided,
                VoidedAt           = @voidedAt,
                VoidReason         = @voidReason
            WHERE Id = @id;
            """);

        Bind(command, redemption);
        command.With("@id", redemption.Id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Redemption> GetBetween(DateTime fromInclusive, DateTime toExclusive)
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM Redemptions
            WHERE OccurredAt >= @from AND OccurredAt < @to
            ORDER BY OccurredAt, Id;
            """);

        command.With("@from", Db.ToText(fromInclusive));
        command.With("@to", Db.ToText(toExclusive));

        return ReadAll(command);
    }

    private static List<Redemption> ReadAll(SqliteCommand command)
    {
        var results = new List<Redemption>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    private static void Bind(SqliteCommand command, Redemption redemption)
    {
        command.With("@customer", redemption.CustomerId);
        command.With("@reward", redemption.RewardId);
        command.With("@rewardName", redemption.RewardName);
        command.With("@kind", (int)redemption.Kind);
        command.With("@points", redemption.PointsSpent);
        command.With("@discount", Db.ToBani(redemption.DiscountValue));
        command.With("@basket", redemption.BasketAmount.HasValue
            ? Db.ToBani(redemption.BasketAmount.Value)
            : DBNull.Value);
        command.With("@receipt", redemption.ReceiptNumber);
        command.With("@transaction", redemption.PointTransactionId);
        command.With("@occurred", Db.ToText(redemption.OccurredAt));
        command.With("@userId", redemption.UserId);
        command.With("@userName", redemption.UserName);
        command.With("@voided", redemption.IsVoided ? 1 : 0);
        command.With("@voidedAt", Db.ToTextOrNull(redemption.VoidedAt));
        command.With("@voidReason", redemption.VoidReason);
    }

    private static Redemption Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt("Id"),
        CustomerId = reader.GetInt("CustomerId"),
        RewardId = reader.GetInt("RewardId"),
        RewardName = reader.GetText("RewardName"),
        Kind = (RewardKind)reader.GetInt("Kind"),
        PointsSpent = reader.GetInt("PointsSpent"),
        DiscountValue = reader.GetMoney("DiscountValueBani"),
        BasketAmount = reader.GetMoneyOrNull("BasketAmountBani"),
        ReceiptNumber = reader.GetTextOrNull("ReceiptNumber"),
        PointTransactionId = reader.GetInt("PointTransactionId"),
        OccurredAt = reader.GetTimestamp("OccurredAt"),
        UserId = reader.GetIntOrNull("UserId"),
        UserName = reader.GetTextOrNull("UserName"),
        IsVoided = reader.GetBool("IsVoided"),
        VoidedAt = reader.GetTimestampOrNull("VoidedAt"),
        VoidReason = reader.GetTextOrNull("VoidReason"),
    };
}
