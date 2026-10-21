using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Sqlite;

/// <summary>
/// Structura bazei de date și aducerea ei la zi.
///
/// Versiunea schemei se ține în „PRAGMA user_version”, un contor propriu al
/// fișierului SQLite. La fiecare pornire se compară cu versiunea așteptată de
/// program și se aplică doar pașii lipsă. Astfel o versiune nouă a aplicației
/// poate fi copiată peste cea veche fără ca magazinul să piardă datele și fără
/// să fie nevoie de vreo operațiune manuală.
/// </summary>
internal static class DatabaseSchema
{
    /// <summary>Se mărește cu unu la fiecare modificare de structură, adăugând un pas în Migrate.</summary>
    public const int CurrentVersion = 3;

    public static void Initialize(SqliteConnection connection)
    {
        var version = GetUserVersion(connection);

        if (version > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Baza de date a fost creată de o versiune mai nouă a programului (schema {version}, " +
                $"programul cunoaște {CurrentVersion}). Actualizează aplicația.");
        }

        if (version == CurrentVersion)
            return;

        using var transaction = connection.BeginTransaction();

        if (version < 1)
        {
            CreateInitialSchema(connection, transaction);
            SeedDefaults(connection, transaction);
        }

        if (version < 2)
            AddPlatformTables(connection, transaction);

        if (version < 3)
            MergeUserNames(connection, transaction);

        // Pașii următori se adaugă aici, fiecare sub propriul „if (version < N)”.

        transaction.Commit();

        SetUserVersion(connection, CurrentVersion);
    }

    private static void CreateInitialSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE Customers (
                Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                CardCode              TEXT    NOT NULL COLLATE NOCASE,
                FirstName             TEXT    NOT NULL,
                LastName              TEXT    NOT NULL,
                Phone                 TEXT    NULL,
                Email                 TEXT    NULL,
                BirthDate             TEXT    NULL,
                EnrolledAt            TEXT    NOT NULL,
                IsActive              INTEGER NOT NULL DEFAULT 1,
                Notes                 TEXT    NULL,
                MarketingConsent      INTEGER NOT NULL DEFAULT 0,
                ConsentAt             TEXT    NULL,
                LastBirthdayBonusYear INTEGER NULL,

                -- Numele, telefonul, e-mailul și codul de card reunite, cu literele
                -- mici și fără diacritice. Există pentru ca o căutare după „gheorghita”
                -- să găsească și „Gheorghiță”: la casă nimeni nu tastează diacritice.
                SearchKey             TEXT    NOT NULL DEFAULT ''
            );

            CREATE UNIQUE INDEX IX_Customers_CardCode ON Customers (CardCode);
            CREATE UNIQUE INDEX IX_Customers_Phone    ON Customers (Phone) WHERE Phone IS NOT NULL;
            CREATE INDEX        IX_Customers_Name     ON Customers (LastName, FirstName);
            CREATE INDEX        IX_Customers_Search   ON Customers (SearchKey);
            CREATE INDEX        IX_Customers_Birth    ON Customers (BirthDate) WHERE BirthDate IS NOT NULL;
            """);

        Execute(connection, transaction, """
            CREATE TABLE Tiers (
                Id                       INTEGER PRIMARY KEY AUTOINCREMENT,
                Name                     TEXT    NOT NULL COLLATE NOCASE,
                ThresholdScaled   INTEGER NOT NULL DEFAULT 0,
                EarnMultiplierMilli      INTEGER NOT NULL DEFAULT 1000,
                StandingDiscountMilli    INTEGER NOT NULL DEFAULT 0,
                ColorHex                 TEXT    NOT NULL DEFAULT '#6B7280',
                SortOrder                INTEGER NOT NULL DEFAULT 0,
                Benefits                 TEXT    NULL
            );

            CREATE UNIQUE INDEX IX_Tiers_Name ON Tiers (Name);
            """);

        Execute(connection, transaction, """
            CREATE TABLE Purchases (
                Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId         INTEGER NOT NULL REFERENCES Customers (Id),
                ReceiptNumber      TEXT    NULL,
                GrossAmountBani    INTEGER NOT NULL,
                DiscountAmountBani INTEGER NOT NULL DEFAULT 0,
                NetAmountBani      INTEGER NOT NULL,
                PointsEarned       INTEGER NOT NULL DEFAULT 0,
                TierAtPurchase     TEXT    NOT NULL DEFAULT '',
                MultiplierMilli    INTEGER NOT NULL DEFAULT 1000,
                OccurredAt         TEXT    NOT NULL,
                RecordedByUserId   INTEGER NULL,
                RecordedByUserName TEXT    NULL,
                IsVoided           INTEGER NOT NULL DEFAULT 0,
                VoidedAt           TEXT    NULL,
                VoidReason         TEXT    NULL
            );

            CREATE INDEX IX_Purchases_Customer ON Purchases (CustomerId, OccurredAt);
            CREATE INDEX IX_Purchases_Date     ON Purchases (OccurredAt);
            CREATE INDEX IX_Purchases_Receipt  ON Purchases (ReceiptNumber) WHERE ReceiptNumber IS NOT NULL;
            """);

        Execute(connection, transaction, """
            CREATE TABLE PointLots (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId     INTEGER NOT NULL REFERENCES Customers (Id),
                Source         INTEGER NOT NULL,
                PurchaseId     INTEGER NULL REFERENCES Purchases (Id),
                PointsOriginal INTEGER NOT NULL,
                PointsRemaining INTEGER NOT NULL,
                EarnedAt       TEXT    NOT NULL,
                ExpiresAt      TEXT    NULL,
                IsExpired      INTEGER NOT NULL DEFAULT 0,

                CHECK (PointsRemaining >= 0),
                CHECK (PointsOriginal  >= 0)
            );

            CREATE INDEX IX_PointLots_Open     ON PointLots (CustomerId, IsExpired, ExpiresAt);
            CREATE INDEX IX_PointLots_Expiry   ON PointLots (ExpiresAt) WHERE IsExpired = 0;
            CREATE INDEX IX_PointLots_Purchase ON PointLots (PurchaseId) WHERE PurchaseId IS NOT NULL;
            """);

        Execute(connection, transaction, """
            CREATE TABLE PointTransactions (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId   INTEGER NOT NULL REFERENCES Customers (Id),
                Type         INTEGER NOT NULL,
                Points       INTEGER NOT NULL,
                BalanceAfter INTEGER NOT NULL DEFAULT 0,
                PointLotId   INTEGER NULL,
                PurchaseId   INTEGER NULL,
                RedemptionId INTEGER NULL,
                OccurredAt   TEXT    NOT NULL,
                UserId       INTEGER NULL,
                UserName     TEXT    NULL,
                Note         TEXT    NULL
            );

            CREATE INDEX IX_PointTx_Customer ON PointTransactions (CustomerId, OccurredAt);
            CREATE INDEX IX_PointTx_Date     ON PointTransactions (OccurredAt);
            """);

        Execute(connection, transaction, """
            CREATE TABLE PointLotConsumptions (
                Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                PointLotId         INTEGER NOT NULL REFERENCES PointLots (Id),
                PointTransactionId INTEGER NOT NULL REFERENCES PointTransactions (Id),
                Points             INTEGER NOT NULL,
                OccurredAt         TEXT    NOT NULL
            );

            CREATE INDEX IX_Consumption_Tx  ON PointLotConsumptions (PointTransactionId);
            CREATE INDEX IX_Consumption_Lot ON PointLotConsumptions (PointLotId);
            """);

        Execute(connection, transaction, """
            CREATE TABLE Rewards (
                Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                Name                  TEXT    NOT NULL,
                Description           TEXT    NULL,
                PointsCost            INTEGER NOT NULL,
                Kind                  INTEGER NOT NULL,
                ValueScaled           INTEGER NOT NULL DEFAULT 0,
                MinPurchaseBani       INTEGER NOT NULL DEFAULT 0,
                MaxDiscountBani       INTEGER NOT NULL DEFAULT 0,
                MinTierId             INTEGER NULL REFERENCES Tiers (Id),
                IsActive              INTEGER NOT NULL DEFAULT 1,
                ValidFrom             TEXT    NULL,
                ValidTo               TEXT    NULL,
                SortOrder             INTEGER NOT NULL DEFAULT 0,

                CHECK (PointsCost >= 0)
            );

            CREATE INDEX IX_Rewards_Active ON Rewards (IsActive, SortOrder);
            """);

        Execute(connection, transaction, """
            CREATE TABLE Redemptions (
                Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId         INTEGER NOT NULL REFERENCES Customers (Id),
                RewardId           INTEGER NOT NULL,
                RewardName         TEXT    NOT NULL,
                Kind               INTEGER NOT NULL,
                PointsSpent        INTEGER NOT NULL,
                DiscountValueBani  INTEGER NOT NULL DEFAULT 0,
                BasketAmountBani   INTEGER NULL,
                ReceiptNumber      TEXT    NULL,
                PointTransactionId INTEGER NOT NULL DEFAULT 0,
                OccurredAt         TEXT    NOT NULL,
                UserId             INTEGER NULL,
                UserName           TEXT    NULL,
                IsVoided           INTEGER NOT NULL DEFAULT 0,
                VoidedAt           TEXT    NULL,
                VoidReason         TEXT    NULL
            );

            CREATE INDEX IX_Redemptions_Customer ON Redemptions (CustomerId, OccurredAt);
            CREATE INDEX IX_Redemptions_Date     ON Redemptions (OccurredAt);
            """);

        Execute(connection, transaction, """
            CREATE TABLE Users (
                Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                Username           TEXT    NOT NULL COLLATE NOCASE,
                DisplayName        TEXT    NOT NULL,
                PasswordHash       TEXT    NOT NULL,
                PasswordSalt       TEXT    NOT NULL,
                PasswordIterations INTEGER NOT NULL,
                Role               INTEGER NOT NULL,
                IsActive           INTEGER NOT NULL DEFAULT 1,
                MustChangePassword INTEGER NOT NULL DEFAULT 0,
                CreatedAt          TEXT    NOT NULL,
                LastLoginAt        TEXT    NULL
            );

            CREATE UNIQUE INDEX IX_Users_Username ON Users (Username);
            """);

        Execute(connection, transaction, """
            CREATE TABLE Settings (
                Key   TEXT NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL
            ) WITHOUT ROWID;
            """);

        Execute(connection, transaction, """
            CREATE TABLE AuditLog (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                OccurredAt TEXT    NOT NULL,
                UserId     INTEGER NULL,
                UserName   TEXT    NULL,
                Action     TEXT    NOT NULL,
                EntityType TEXT    NULL,
                EntityId   INTEGER NULL,
                Details    TEXT    NULL
            );

            CREATE INDEX IX_Audit_Date ON AuditLog (OccurredAt);
            """);
    }

    /// <summary>
    /// Valorile de pornire: nivelurile, un catalog de recompense potrivit unui
    /// magazin de haine și setările implicite. Nu se creează niciun cont de
    /// utilizator — primul administrator este creat de magazin la prima pornire,
    /// ca programul să nu fie livrat cu o parolă cunoscută.
    /// </summary>
    private static void SeedDefaults(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            INSERT INTO Tiers (Name, ThresholdScaled, EarnMultiplierMilli, StandingDiscountMilli, ColorHex, SortOrder, Benefits)
            VALUES
                ('Bronze',  0,      1000, 0,    '#A9754A', 1, 'Acumulare standard de puncte. Acces la catalogul de recompense.'),
                ('Silver',  100000, 1250, 0,    '#8A93A6', 2, 'Cu 25% mai multe puncte la fiecare cumpărare. Acces la recompense pentru nivelul Silver.'),
                ('Gold',    300000, 1500, 3000, '#C9A227', 3, 'Cu 50% mai multe puncte, reducere permanentă de 3% și acces la toate recompensele.');
            """);

        Execute(connection, transaction, """
            INSERT INTO Rewards (Name, Description, PointsCost, Kind, ValueScaled, MinPurchaseBani, MaxDiscountBani, MinTierId, IsActive, SortOrder)
            VALUES
                ('Reducere 10 lei',  'Se scad 10 lei din valoarea bonului.',              100, 1, 1000,  5000,  0,     NULL, 1, 1),
                ('Reducere 25 lei',  'Se scad 25 lei din valoarea bonului.',              250, 1, 2500,  10000, 0,     NULL, 1, 2),
                ('Reducere 50 lei',  'Se scad 50 lei din valoarea bonului.',              500, 1, 5000,  20000, 0,     NULL, 1, 3),
                ('Accesoriu gratuit','Un accesoriu din zona marcată, în limita a 40 lei.', 300, 3, 4000,  0,     0,     NULL, 1, 4),
                ('Reducere 10%',     'Zece procente din valoarea bonului, maximum 100 lei.', 400, 2, 1000, 15000, 10000, NULL, 1, 5),
                ('Reducere 15% Gold','Rezervată nivelului Gold. Maximum 200 lei.',        700, 2, 1500,  20000, 20000,
                    (SELECT Id FROM Tiers WHERE Name = 'Gold'), 1, 6);
            """);

        Execute(connection, transaction, """
            INSERT INTO Settings (Key, Value) VALUES
                ('earn.lei_per_point',      '10'),
                ('earn.min_purchase',       '0'),
                ('redeem.point_value_lei',  '0.10'),
                ('redeem.minimum_points',   '100'),
                ('expiry.validity_months',  '12'),
                ('expiry.warning_days',     '30'),
                ('bonus.welcome_points',    '50'),
                ('bonus.birthday_points',   '100'),
                ('bonus.birthday_window_days', '7'),
                ('tier.qualifying_months',  '12'),
                ('store.name',              'Magazinul meu'),
                ('store.program_name',      'Club Fidelitate');
            """);
    }

    /// <summary>
    /// Pasul 2 al schemei: campaniile promoționale și câmpurile definite de magazin
    /// pe fișa clientului.
    ///
    /// Toate modificările sunt aditive — tabele noi și coloane noi cu valori
    /// implicite — deci o bază de date existentă se actualizează fără să piardă
    /// nimic și fără vreo intervenție manuală la magazin.
    /// </summary>
    private static void AddPlatformTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE Campaigns (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                Name             TEXT    NOT NULL,
                Description      TEXT    NULL,
                StartsOn         TEXT    NOT NULL,
                EndsOn           TEXT    NOT NULL,
                MultiplierMilli  INTEGER NOT NULL DEFAULT 2000,
                MinPurchaseBani  INTEGER NOT NULL DEFAULT 0,
                DaysOfWeekMask   INTEGER NOT NULL DEFAULT 0,
                IsActive         INTEGER NOT NULL DEFAULT 1,

                CHECK (MultiplierMilli > 0)
            );

            CREATE INDEX IX_Campaigns_Period ON Campaigns (StartsOn, EndsOn) WHERE IsActive = 1;
            """);

        Execute(connection, transaction, """
            CREATE TABLE CustomFields (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                Label            TEXT    NOT NULL,
                FieldKey         TEXT    NOT NULL COLLATE NOCASE,
                Kind             INTEGER NOT NULL DEFAULT 1,
                Options          TEXT    NULL,
                IsRequired       INTEGER NOT NULL DEFAULT 0,
                ShowInList       INTEGER NOT NULL DEFAULT 0,
                IncludeInSearch  INTEGER NOT NULL DEFAULT 1,
                SortOrder        INTEGER NOT NULL DEFAULT 0,
                IsActive         INTEGER NOT NULL DEFAULT 1
            );

            CREATE UNIQUE INDEX IX_CustomFields_Key ON CustomFields (FieldKey);
            """);

        Execute(connection, transaction, """
            CREATE TABLE CustomerFieldValues (
                CustomerId  INTEGER NOT NULL REFERENCES Customers (Id),
                FieldId     INTEGER NOT NULL REFERENCES CustomFields (Id),
                Value       TEXT    NULL,

                -- Aceeasi valoare, cu litere mici si fara diacritice, ca sa intre
                -- in cautarea rapida de la casa in aceleasi conditii ca numele.
                SearchValue TEXT    NULL,

                PRIMARY KEY (CustomerId, FieldId)
            ) WITHOUT ROWID;

            CREATE INDEX IX_FieldValues_Field  ON CustomerFieldValues (FieldId);
            CREATE INDEX IX_FieldValues_Search ON CustomerFieldValues (SearchValue);
            """);

        // Campania aplicată se reține pe fiecare bon, ca refacerea calculului să
        // fie posibilă și după ce campania a expirat sau a fost ștearsă.
        Execute(connection, transaction, """
            ALTER TABLE Purchases ADD COLUMN CampaignName TEXT NULL;
            ALTER TABLE Purchases ADD COLUMN CampaignMultiplierMilli INTEGER NOT NULL DEFAULT 1000;
            """);

        // Valorile implicite ale setărilor noi. „DO NOTHING” protejează o bază de
        // date existentă în care administratorul le-ar fi schimbat deja.
        Execute(connection, transaction, """
            INSERT INTO Settings (Key, Value) VALUES
                ('tier.basis',    '1'),
                ('card.prefix',   ''),
                ('card.digits',   '6'),
                ('brand.accent',  '#3B5BDB')
            ON CONFLICT (Key) DO NOTHING;
            """);
    }

    /// <summary>
    /// Pasul 3: conturile aveau două nume — unul de autentificare și unul afișat.
    ///
    /// Separarea nu își merita costul într-un magazin cu câțiva angajați: două
    /// câmpuri obligatorii la fiecare cont și întrebarea inevitabilă „care nume îl
    /// scriu la intrare”. Rămâne unul singur, cel care se și tastează, și se și
    /// citește în rapoarte.
    ///
    /// La migrare câștigă numele afișat, fiindcă el este cel scris pentru oameni:
    /// contul „cristi” cu numele „Cristi Popescu” devine „Cristi Popescu”. Dacă
    /// numele afișat este gol sau ar duce la un duplicat, contul își păstrează
    /// numele de autentificare — nimeni nu trebuie să rămână pe dinafară după
    /// o actualizare.
    /// </summary>
    private static void MergeUserNames(SqliteConnection connection, SqliteTransaction transaction)
    {
        var users = new List<(int Id, string Username, string Display)>();

        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT Id, Username, IFNULL(DisplayName, '') FROM Users ORDER BY Id;
                """;

            using var reader = read.ExecuteReader();
            while (reader.Read())
                users.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, username, display) in users)
        {
            var candidate = string.IsNullOrWhiteSpace(display) ? username : display.Trim();

            // Numele afișate nu erau unice, deci două conturi pot ajunge la același
            // text. Primul îl primește, ceilalți rămân cu numele lor de autentificare.
            if (taken.Contains(candidate))
                candidate = username;

            // Dacă nici acela nu mai este liber, se numerotează până la unul care este.
            var final = candidate;
            var suffix = 2;
            while (taken.Contains(final))
                final = candidate + " " + suffix++;

            taken.Add(final);

            if (final == username)
                continue;

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Users SET Username = @name WHERE Id = @id;";

            var nameParam = update.CreateParameter();
            nameParam.ParameterName = "@name";
            nameParam.Value = final;
            update.Parameters.Add(nameParam);

            var idParam = update.CreateParameter();
            idParam.ParameterName = "@id";
            idParam.Value = id;
            update.Parameters.Add(idParam);

            update.ExecuteNonQuery();
        }

        Execute(connection, transaction, "ALTER TABLE Users DROP COLUMN DisplayName;");
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void SetUserVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        // PRAGMA nu acceptă parametri, dar valoarea este o constantă a programului.
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }
}
