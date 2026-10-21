using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Repositories;

internal sealed class CampaignRepository : RepositoryBase, ICampaignRepository
{
    private const string Columns =
        "Id, Name, Description, StartsOn, EndsOn, MultiplierMilli, MinPurchaseBani, DaysOfWeekMask, IsActive";

    public CampaignRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public IReadOnlyList<Campaign> GetAll()
    {
        using var command = Command($"SELECT {Columns} FROM Campaigns ORDER BY StartsOn DESC, Id DESC;");
        return ReadAll(command);
    }

    public Campaign? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM Campaigns WHERE Id = @id;");
        command.With("@id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Campaign> GetRunningOn(DateTime day)
    {
        // Filtrarea pe ziua săptămânii și pe valoarea bonului se face în memorie:
        // sunt puține campanii, iar regula este mai ușor de citit în cod decât în SQL.
        using var command = Command($"""
            SELECT {Columns}
            FROM Campaigns
            WHERE IsActive = 1 AND StartsOn <= @day AND EndsOn >= @day
            ORDER BY MultiplierMilli DESC;
            """);

        command.With("@day", Db.ToDateText(day));
        return ReadAll(command);
    }

    public int Insert(Campaign campaign)
    {
        using var command = Command("""
            INSERT INTO Campaigns
                (Name, Description, StartsOn, EndsOn, MultiplierMilli, MinPurchaseBani, DaysOfWeekMask, IsActive)
            VALUES
                (@name, @description, @starts, @ends, @multiplier, @minPurchase, @days, @active)
            RETURNING Id;
            """);

        Bind(command, campaign);
        return ExecuteInsert(command);
    }

    public void Update(Campaign campaign)
    {
        using var command = Command("""
            UPDATE Campaigns SET
                Name            = @name,
                Description     = @description,
                StartsOn        = @starts,
                EndsOn          = @ends,
                MultiplierMilli = @multiplier,
                MinPurchaseBani = @minPurchase,
                DaysOfWeekMask  = @days,
                IsActive        = @active
            WHERE Id = @id;
            """);

        Bind(command, campaign);
        command.With("@id", campaign.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        // Bonurile păstrează numele campaniei aplicate, deci ștergerea din listă
        // nu schimbă cu nimic istoricul deja înregistrat.
        using var command = Command("DELETE FROM Campaigns WHERE Id = @id;");
        command.With("@id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, Campaign campaign)
    {
        command.With("@name", campaign.Name);
        command.With("@description", campaign.Description);
        command.With("@starts", Db.ToDateText(campaign.StartsOn));
        command.With("@ends", Db.ToDateText(campaign.EndsOn));
        command.With("@multiplier", Db.ToMilli(campaign.Multiplier));
        command.With("@minPurchase", Db.ToBani(campaign.MinPurchaseAmount));
        command.With("@days", campaign.DaysOfWeekMask);
        command.With("@active", campaign.IsActive ? 1 : 0);
    }

    private static List<Campaign> ReadAll(SqliteCommand command)
    {
        var results = new List<Campaign>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    private static Campaign Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt("Id"),
        Name = reader.GetText("Name"),
        Description = reader.GetTextOrNull("Description"),
        StartsOn = reader.GetDateOrNull("StartsOn") ?? DateTime.MinValue,
        EndsOn = reader.GetDateOrNull("EndsOn") ?? DateTime.MaxValue,
        Multiplier = reader.GetMilli("MultiplierMilli"),
        MinPurchaseAmount = reader.GetMoney("MinPurchaseBani"),
        DaysOfWeekMask = reader.GetInt("DaysOfWeekMask"),
        IsActive = reader.GetBool("IsActive"),
    };
}

internal sealed class CustomFieldRepository : RepositoryBase, ICustomFieldRepository
{
    private const string Columns =
        "Id, Label, FieldKey, Kind, Options, IsRequired, ShowInList, IncludeInSearch, SortOrder, IsActive";

    public CustomFieldRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public IReadOnlyList<CustomFieldDefinition> GetDefinitions(bool includeInactive = false)
    {
        using var command = Command($"""
            SELECT {Columns}
            FROM CustomFields
            WHERE (@all = 1 OR IsActive = 1)
            ORDER BY SortOrder, Id;
            """);

        command.With("@all", includeInactive ? 1 : 0);

        var results = new List<CustomFieldDefinition>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    public CustomFieldDefinition? GetDefinition(int id)
    {
        using var command = Command($"SELECT {Columns} FROM CustomFields WHERE Id = @id;");
        command.With("@id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public int InsertDefinition(CustomFieldDefinition definition)
    {
        using var command = Command("""
            INSERT INTO CustomFields
                (Label, FieldKey, Kind, Options, IsRequired, ShowInList, IncludeInSearch, SortOrder, IsActive)
            VALUES
                (@label, @key, @kind, @options, @required, @showInList, @search, @sort, @active)
            RETURNING Id;
            """);

        Bind(command, definition);
        return ExecuteInsert(command);
    }

    public void UpdateDefinition(CustomFieldDefinition definition)
    {
        using var command = Command("""
            UPDATE CustomFields SET
                Label           = @label,
                FieldKey        = @key,
                Kind            = @kind,
                Options         = @options,
                IsRequired      = @required,
                ShowInList      = @showInList,
                IncludeInSearch = @search,
                SortOrder       = @sort,
                IsActive        = @active
            WHERE Id = @id;
            """);

        Bind(command, definition);
        command.With("@id", definition.Id);
        command.ExecuteNonQuery();
    }

    public void DeleteDefinition(int id)
    {
        // Valorile completate se șterg odată cu definiția: fără ea nu ar mai avea
        // niciun înțeles și ar rămâne date orfane în baza de date.
        using var values = Command("DELETE FROM CustomerFieldValues WHERE FieldId = @id;");
        values.With("@id", id);
        values.ExecuteNonQuery();

        using var command = Command("DELETE FROM CustomFields WHERE Id = @id;");
        command.With("@id", id);
        command.ExecuteNonQuery();
    }

    public bool KeyExists(string key, int? exceptId = null)
    {
        using var command = Command(
            "SELECT COUNT(*) FROM CustomFields WHERE FieldKey = @key AND (@except IS NULL OR Id <> @except);");

        command.With("@key", key.Trim());
        command.With("@except", exceptId);

        return ScalarInt(command) > 0;
    }

    public IReadOnlyDictionary<int, string?> GetValues(int customerId)
    {
        using var command = Command(
            "SELECT FieldId, Value FROM CustomerFieldValues WHERE CustomerId = @customer;");

        command.With("@customer", customerId);

        var result = new Dictionary<int, string?>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result[reader.GetInt("FieldId")] = reader.GetTextOrNull("Value");

        return result;
    }

    public void SetValues(int customerId, IReadOnlyDictionary<int, string?> values)
    {
        foreach (var (fieldId, value) in values)
        {
            // O valoare golită înseamnă ștergerea rândului, nu păstrarea unui text
            // vid: altfel tabelul ar crește cu date care nu spun nimic.
            if (string.IsNullOrWhiteSpace(value))
            {
                using var delete = Command(
                    "DELETE FROM CustomerFieldValues WHERE CustomerId = @customer AND FieldId = @field;");

                delete.With("@customer", customerId);
                delete.With("@field", fieldId);
                delete.ExecuteNonQuery();
                continue;
            }

            using var upsert = Command("""
                INSERT INTO CustomerFieldValues (CustomerId, FieldId, Value, SearchValue)
                VALUES (@customer, @field, @value, @search)
                ON CONFLICT (CustomerId, FieldId) DO UPDATE SET
                    Value       = excluded.Value,
                    SearchValue = excluded.SearchValue;
                """);

            upsert.With("@customer", customerId);
            upsert.With("@field", fieldId);
            upsert.With("@value", value.Trim());
            upsert.With("@search", SearchKey.Normalize(value));
            upsert.ExecuteNonQuery();
        }
    }

    private static void Bind(SqliteCommand command, CustomFieldDefinition definition)
    {
        command.With("@label", definition.Label);
        command.With("@key", definition.Key);
        command.With("@kind", (int)definition.Kind);
        command.With("@options", definition.Options);
        command.With("@required", definition.IsRequired ? 1 : 0);
        command.With("@showInList", definition.ShowInList ? 1 : 0);
        command.With("@search", definition.IncludeInSearch ? 1 : 0);
        command.With("@sort", definition.SortOrder);
        command.With("@active", definition.IsActive ? 1 : 0);
    }

    private static CustomFieldDefinition Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt("Id"),
        Label = reader.GetText("Label"),
        Key = reader.GetText("FieldKey"),
        Kind = (CustomFieldKind)reader.GetInt("Kind"),
        Options = reader.GetTextOrNull("Options"),
        IsRequired = reader.GetBool("IsRequired"),
        ShowInList = reader.GetBool("ShowInList"),
        IncludeInSearch = reader.GetBool("IncludeInSearch"),
        SortOrder = reader.GetInt("SortOrder"),
        IsActive = reader.GetBool("IsActive"),
    };
}
