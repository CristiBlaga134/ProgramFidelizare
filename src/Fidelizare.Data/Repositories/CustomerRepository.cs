using System.Globalization;
using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Repositories;

internal sealed class CustomerRepository : RepositoryBase, ICustomerRepository
{
    private const string Columns =
        "Id, CardCode, FirstName, LastName, Phone, Email, BirthDate, EnrolledAt, IsActive, " +
        "Notes, MarketingConsent, ConsentAt, LastBirthdayBonusYear";

    public CustomerRepository(SqliteConnection connection, SqliteTransaction transaction)
        : base(connection, transaction)
    {
    }

    public Customer? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM Customers WHERE Id = @id;");
        command.With("@id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Customer? GetByCardCode(string cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            return null;

        using var command = Command($"SELECT {Columns} FROM Customers WHERE CardCode = @code;");
        command.With("@code", cardCode.Trim());

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Customer? GetByPhone(string normalizedPhone)
    {
        if (string.IsNullOrWhiteSpace(normalizedPhone))
            return null;

        using var command = Command($"SELECT {Columns} FROM Customers WHERE Phone = @phone;");
        command.With("@phone", normalizedPhone);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Customer> Search(CustomerQuery query)
    {
        var (where, parameters) = BuildFilter(query);

        using var command = Command($"""
            SELECT {Columns}
            FROM Customers
            {where}
            ORDER BY LastName COLLATE NOCASE, FirstName COLLATE NOCASE
            LIMIT @take OFFSET @skip;
            """);

        foreach (var (name, value) in parameters)
            command.With(name, value);

        command.With("@take", query.Take <= 0 ? 200 : query.Take);
        command.With("@skip", Math.Max(0, query.Skip));

        var results = new List<Customer>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    public int Count(CustomerQuery query)
    {
        var (where, parameters) = BuildFilter(query);

        using var command = Command($"SELECT COUNT(*) FROM Customers {where};");
        foreach (var (name, value) in parameters)
            command.With(name, value);

        return ScalarInt(command);
    }

    public int Insert(Customer customer)
    {
        using var command = Command("""
            INSERT INTO Customers
                (CardCode, FirstName, LastName, Phone, Email, BirthDate, EnrolledAt, IsActive,
                 Notes, MarketingConsent, ConsentAt, LastBirthdayBonusYear, SearchKey)
            VALUES
                (@card, @first, @last, @phone, @email, @birth, @enrolled, @active,
                 @notes, @consent, @consentAt, @bonusYear, @search)
            RETURNING Id;
            """);

        Bind(command, customer);
        return ExecuteInsert(command);
    }

    public void Update(Customer customer)
    {
        using var command = Command("""
            UPDATE Customers SET
                CardCode              = @card,
                FirstName             = @first,
                LastName              = @last,
                Phone                 = @phone,
                Email                 = @email,
                BirthDate             = @birth,
                EnrolledAt            = @enrolled,
                IsActive              = @active,
                Notes                 = @notes,
                MarketingConsent      = @consent,
                ConsentAt             = @consentAt,
                LastBirthdayBonusYear = @bonusYear,
                SearchKey             = @search
            WHERE Id = @id;
            """);

        Bind(command, customer);
        command.With("@id", customer.Id);
        command.ExecuteNonQuery();
    }

    public bool CardCodeExists(string cardCode, int? exceptCustomerId = null)
    {
        using var command = Command(
            "SELECT COUNT(*) FROM Customers WHERE CardCode = @code AND (@except IS NULL OR Id <> @except);");

        command.With("@code", cardCode.Trim());
        command.With("@except", exceptCustomerId);

        return ScalarInt(command) > 0;
    }

    public bool PhoneExists(string normalizedPhone, int? exceptCustomerId = null)
    {
        using var command = Command(
            "SELECT COUNT(*) FROM Customers WHERE Phone = @phone AND (@except IS NULL OR Id <> @except);");

        command.With("@phone", normalizedPhone);
        command.With("@except", exceptCustomerId);

        return ScalarInt(command) > 0;
    }

    public long GetMaxNumericCardCode()
    {
        // Se iau în calcul doar codurile formate exclusiv din cifre; un cod importat
        // de forma „VIP-12” nu trebuie să influențeze numerotarea automată.
        using var command = Command("""
            SELECT IFNULL(MAX(CAST(CardCode AS INTEGER)), 0)
            FROM Customers
            WHERE CardCode GLOB '[0-9]*' AND CardCode NOT GLOB '*[^0-9]*';
            """);

        return ScalarLong(command);
    }

    public IReadOnlyList<Customer> GetBirthdaysBetween(DateTime fromInclusive, DateTime toInclusive)
    {
        // Se compară doar luna și ziua, ca aniversarea să fie găsită indiferent de an.
        var fromKey = fromInclusive.ToString("MM-dd", CultureInfo.InvariantCulture);
        var toKey = toInclusive.ToString("MM-dd", CultureInfo.InvariantCulture);

        // Când intervalul trece peste 31 decembrie, „de la” devine mai mare decât
        // „până la”, iar condiția trebuie despărțită în două capete de an.
        var condition = string.CompareOrdinal(fromKey, toKey) <= 0
            ? "SUBSTR(BirthDate, 6, 5) BETWEEN @from AND @to"
            : "(SUBSTR(BirthDate, 6, 5) >= @from OR SUBSTR(BirthDate, 6, 5) <= @to)";

        using var command = Command($"""
            SELECT {Columns}
            FROM Customers
            WHERE BirthDate IS NOT NULL AND {condition}
            ORDER BY SUBSTR(BirthDate, 6, 5), LastName COLLATE NOCASE;
            """);

        command.With("@from", fromKey);
        command.With("@to", toKey);

        var results = new List<Customer>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));

        return results;
    }

    private static (string Where, List<(string Name, object? Value)> Parameters) BuildFilter(CustomerQuery query)
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object?)>();

        if (!query.IncludeInactive)
            conditions.Add("IsActive = 1");

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            // Căutarea acoperă și valorile câmpurilor definite de magazin, dacă
            // acestea au fost marcate ca fiind căutabile.
            conditions.Add("""
                (SearchKey LIKE @like OR EXISTS (
                    SELECT 1
                    FROM CustomerFieldValues v
                    JOIN CustomFields f ON f.Id = v.FieldId
                    WHERE v.CustomerId = Customers.Id
                      AND f.IncludeInSearch = 1
                      AND v.SearchValue LIKE @like))
                """);

            parameters.Add(("@like", $"%{SearchKey.Normalize(query.SearchText)}%"));
        }

        return conditions.Count == 0
            ? (string.Empty, parameters)
            : ("WHERE " + string.Join(" AND ", conditions), parameters);
    }

    private static void Bind(SqliteCommand command, Customer customer)
    {
        command.With("@card", customer.CardCode);
        command.With("@first", customer.FirstName);
        command.With("@last", customer.LastName);
        command.With("@phone", customer.Phone);
        command.With("@email", customer.Email);
        command.With("@birth", Db.ToDateTextOrNull(customer.BirthDate));
        command.With("@enrolled", Db.ToText(customer.EnrolledAt));
        command.With("@active", customer.IsActive ? 1 : 0);
        command.With("@notes", customer.Notes);
        command.With("@consent", customer.MarketingConsent ? 1 : 0);
        command.With("@consentAt", Db.ToTextOrNull(customer.ConsentAt));
        command.With("@bonusYear", customer.LastBirthdayBonusYear);
        command.With("@search", SearchKey.ForCustomer(
            customer.LastName, customer.FirstName, customer.Phone, customer.Email, customer.CardCode));
    }

    private static Customer Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt("Id"),
        CardCode = reader.GetText("CardCode"),
        FirstName = reader.GetText("FirstName"),
        LastName = reader.GetText("LastName"),
        Phone = reader.GetTextOrNull("Phone"),
        Email = reader.GetTextOrNull("Email"),
        BirthDate = reader.GetDateOrNull("BirthDate"),
        EnrolledAt = reader.GetTimestamp("EnrolledAt"),
        IsActive = reader.GetBool("IsActive"),
        Notes = reader.GetTextOrNull("Notes"),
        MarketingConsent = reader.GetBool("MarketingConsent"),
        ConsentAt = reader.GetTimestampOrNull("ConsentAt"),
        LastBirthdayBonusYear = reader.GetIntOrNull("LastBirthdayBonusYear"),
    };
}
