using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Repositories;

internal sealed class SettingsRepository : RepositoryBase, ISettingsRepository
{
    public SettingsRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public string? Get(string key)
    {
        using var command = Command("SELECT Value FROM Settings WHERE Key = @key;");
        command.With("@key", key);

        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        using var command = Command("SELECT Key, Value FROM Settings;");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result[reader.GetText("Key")] = reader.GetText("Value");

        return result;
    }

    public void Set(string key, string value)
    {
        using var command = Command("""
            INSERT INTO Settings (Key, Value) VALUES (@key, @value)
            ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
            """);

        command.With("@key", key);
        command.With("@value", value);
        command.ExecuteNonQuery();
    }
}

internal sealed class UserRepository : RepositoryBase, IUserRepository
{
    private const string Columns =
        "Id, Username, PasswordHash, PasswordSalt, PasswordIterations, Role, " +
        "IsActive, MustChangePassword, CreatedAt, LastLoginAt";

    public UserRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public AppUser? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM Users WHERE Id = @id;");
        command.With("@id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public AppUser? GetByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        using var command = Command($"SELECT {Columns} FROM Users WHERE Username = @username;");
        command.With("@username", username.Trim());

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<AppUser> GetAll()
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM Users
            ORDER BY IsActive DESC, Role DESC, Username COLLATE NOCASE;
            """);

        var results = new List<AppUser>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    public int Insert(AppUser user)
    {
        using var command = Command("""
            INSERT INTO Users
                (Username, PasswordHash, PasswordSalt, PasswordIterations, Role,
                 IsActive, MustChangePassword, CreatedAt, LastLoginAt)
            VALUES
                (@username, @hash, @salt, @iterations, @role,
                 @active, @mustChange, @created, @lastLogin)
            RETURNING Id;
            """);

        Bind(command, user);
        return ExecuteInsert(command);
    }

    public void Update(AppUser user)
    {
        using var command = Command("""
            UPDATE Users SET
                Username           = @username,
                PasswordHash       = @hash,
                PasswordSalt       = @salt,
                PasswordIterations = @iterations,
                Role               = @role,
                IsActive           = @active,
                MustChangePassword = @mustChange,
                CreatedAt          = @created,
                LastLoginAt        = @lastLogin
            WHERE Id = @id;
            """);

        Bind(command, user);
        command.With("@id", user.Id);
        command.ExecuteNonQuery();
    }

    public bool UsernameExists(string username, int? exceptUserId = null)
    {
        using var command = Command(
            "SELECT COUNT(*) FROM Users WHERE Username = @username AND (@except IS NULL OR Id <> @except);");

        command.With("@username", username.Trim());
        command.With("@except", exceptUserId);

        return ScalarInt(command) > 0;
    }

    public int CountActiveAdmins(int? exceptUserId = null)
    {
        using var command = Command("""
            SELECT COUNT(*)
            FROM Users
            WHERE Role = @role AND IsActive = 1 AND (@except IS NULL OR Id <> @except);
            """);

        command.With("@role", (int)UserRole.Admin);
        command.With("@except", exceptUserId);

        return ScalarInt(command);
    }

    private static void Bind(SqliteCommand command, AppUser user)
    {
        command.With("@username", user.Username);
        command.With("@hash", user.PasswordHash);
        command.With("@salt", user.PasswordSalt);
        command.With("@iterations", user.PasswordIterations);
        command.With("@role", (int)user.Role);
        command.With("@active", user.IsActive ? 1 : 0);
        command.With("@mustChange", user.MustChangePassword ? 1 : 0);
        command.With("@created", Db.ToText(user.CreatedAt));
        command.With("@lastLogin", Db.ToTextOrNull(user.LastLoginAt));
    }

    private static AppUser Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt("Id"),
        Username = reader.GetText("Username"),
        PasswordHash = reader.GetText("PasswordHash"),
        PasswordSalt = reader.GetText("PasswordSalt"),
        PasswordIterations = reader.GetInt("PasswordIterations"),
        Role = (UserRole)reader.GetInt("Role"),
        IsActive = reader.GetBool("IsActive"),
        MustChangePassword = reader.GetBool("MustChangePassword"),
        CreatedAt = reader.GetTimestamp("CreatedAt"),
        LastLoginAt = reader.GetTimestampOrNull("LastLoginAt"),
    };
}

internal sealed class AuditRepository : RepositoryBase, IAuditRepository
{
    private const string Columns = "Id, OccurredAt, UserId, UserName, Action, EntityType, EntityId, Details";

    public AuditRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public int Insert(AuditEntry entry)
    {
        using var command = Command("""
            INSERT INTO AuditLog (OccurredAt, UserId, UserName, Action, EntityType, EntityId, Details)
            VALUES (@occurred, @userId, @userName, @action, @entityType, @entityId, @details)
            RETURNING Id;
            """);

        command.With("@occurred", Db.ToText(entry.OccurredAt));
        command.With("@userId", entry.UserId);
        command.With("@userName", entry.UserName);
        command.With("@action", entry.Action);
        command.With("@entityType", entry.EntityType);
        command.With("@entityId", entry.EntityId);
        command.With("@details", entry.Details);

        return ExecuteInsert(command);
    }

    public IReadOnlyList<AuditEntry> GetRecent(int take = 500)
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM AuditLog
            ORDER BY OccurredAt DESC, Id DESC
            LIMIT @take;
            """);

        command.With("@take", take);
        return ReadAll(command);
    }

    public int ReplaceInDetails(string find, string replacement)
    {
        using var command = Command("""
            UPDATE AuditLog
            SET Details = REPLACE(Details, @find, @replacement)
            WHERE INSTR(Details, @find) > 0;
            """);

        command.With("@find", find);
        command.With("@replacement", replacement);
        return command.ExecuteNonQuery();
    }

    public IReadOnlyList<AuditEntry> GetBetween(DateTime fromInclusive, DateTime toExclusive)
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM AuditLog
            WHERE OccurredAt >= @from AND OccurredAt < @to
            ORDER BY OccurredAt DESC, Id DESC;
            """);

        command.With("@from", Db.ToText(fromInclusive));
        command.With("@to", Db.ToText(toExclusive));

        return ReadAll(command);
    }

    private static List<AuditEntry> ReadAll(SqliteCommand command)
    {
        var results = new List<AuditEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new AuditEntry
            {
                Id = reader.GetInt("Id"),
                OccurredAt = reader.GetTimestamp("OccurredAt"),
                UserId = reader.GetIntOrNull("UserId"),
                UserName = reader.GetTextOrNull("UserName"),
                Action = reader.GetText("Action"),
                EntityType = reader.GetTextOrNull("EntityType"),
                EntityId = reader.GetIntOrNull("EntityId"),
                Details = reader.GetTextOrNull("Details"),
            });
        }

        return results;
    }
}
