using System.Globalization;
using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;

namespace Fidelizare.Core.Services;

/// <summary>O problemă găsită pe o linie din fișierul de import.</summary>
public sealed class ImportIssue
{
    public required int Line { get; init; }
    public required string Message { get; init; }

    public override string ToString() => $"Linia {Line}: {Message}";
}

/// <summary>Ce s-ar întâmpla dacă importul ar fi executat, fără a scrie nimic.</summary>
public sealed class ImportPreview
{
    public int TotalRows { get; init; }
    public int WillCreate { get; init; }
    public int WillSkip { get; init; }
    public IReadOnlyList<ImportIssue> Issues { get; init; } = [];
    public IReadOnlyList<string> RecognizedColumns { get; init; } = [];
    public IReadOnlyList<string> IgnoredColumns { get; init; } = [];

    public bool CanImport => WillCreate > 0;
}

public sealed class ImportOutcome
{
    public int Created { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int PointsGranted { get; init; }
    public IReadOnlyList<ImportIssue> Issues { get; init; } = [];
}

/// <summary>
/// Aducerea datelor în program și scoaterea lor din el.
///
/// Un magazin care trece de la un caiet sau de la un fișier Excel nu poate
/// reintroduce manual câteva sute de clienți; iar unul care ține programul nu
/// trebuie să rămână prizonier în el. Ambele drumuri trec prin CSV, formatul pe
/// care orice program de contabilitate îl citește.
/// </summary>
public sealed class DataTransferService
{
    /// <summary>Numele acceptate pentru coloanele standard. Primul este cel scris la export.</summary>
    private static readonly (string Canonical, string[] Aliases)[] KnownColumns =
    [
        ("Nume", ["nume", "numedefamilie", "familie"]),
        ("Prenume", ["prenume"]),
        ("Telefon", ["telefon", "tel", "mobil"]),
        ("Email", ["email", "mail", "eposta"]),
        ("DataNasterii", ["datanasterii", "datanastere", "nastere", "zidenastere"]),
        ("CodCard", ["codcard", "card", "cod"]),
        ("Puncte", ["puncte", "soldpuncte", "puncteinitiale"]),
        ("Observatii", ["observatii", "note", "mentiuni"]),
    ];

    private readonly IUnitOfWorkFactory _factory;
    private readonly LoyaltyService _loyalty;
    private readonly IClock _clock;

    public DataTransferService(IUnitOfWorkFactory factory, LoyaltyService loyalty, IClock clock)
    {
        _factory = factory;
        _loyalty = loyalty;
        _clock = clock;
    }

    // ============================================================== Import

    /// <summary>Citește fișierul și raportează ce s-ar întâmpla, fără să scrie nimic.</summary>
    public OperationResult<ImportPreview> PreviewCustomers(string path)
    {
        CsvFile.ReadResult file;

        try
        {
            file = CsvFile.Read(path);
        }
        catch (Exception ex)
        {
            return OperationResult<ImportPreview>.Fail($"Fișierul nu a putut fi citit: {ex.Message}");
        }

        if (file.Rows.Count == 0)
            return OperationResult<ImportPreview>.Fail("Fișierul nu conține niciun rând de date.");

        using var uow = _factory.Create();
        var fields = uow.CustomFields.GetDefinitions();

        var issues = new List<ImportIssue>();
        var willCreate = 0;
        var willSkip = 0;

        // Telefoanele și codurile deja întâlnite în fișier, ca să prindem și
        // duplicatele din interiorul lui, nu doar pe cele față de baza de date.
        var seenPhones = new HashSet<string>(StringComparer.Ordinal);
        var seenCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in file.Rows)
        {
            var parsed = ParseRow(row, fields);

            if (parsed.Error is not null)
            {
                issues.Add(new ImportIssue { Line = row.LineNumber, Message = parsed.Error });
                willSkip++;
                continue;
            }

            var customer = parsed.Customer!;
            var duplicate = FindDuplicate(uow, customer, seenPhones, seenCards);

            if (duplicate is not null)
            {
                issues.Add(new ImportIssue { Line = row.LineNumber, Message = duplicate });
                willSkip++;
                continue;
            }

            if (!string.IsNullOrEmpty(customer.Phone))
                seenPhones.Add(customer.Phone);
            if (!string.IsNullOrWhiteSpace(customer.CardCode))
                seenCards.Add(customer.CardCode);

            willCreate++;
        }

        var recognized = new List<string>();
        var ignored = new List<string>();

        foreach (var header in file.Headers)
        {
            if (MatchColumn(header) is not null || fields.Any(f => MatchesField(header, f)))
                recognized.Add(header);
            else if (!string.IsNullOrWhiteSpace(header))
                ignored.Add(header);
        }

        return OperationResult<ImportPreview>.Ok(new ImportPreview
        {
            TotalRows = file.Rows.Count,
            WillCreate = willCreate,
            WillSkip = willSkip,
            Issues = issues,
            RecognizedColumns = recognized,
            IgnoredColumns = ignored,
        });
    }

    /// <summary>
    /// Execută importul. Fiecare client este înscris separat, deci o linie greșită
    /// nu anulează munca celorlalte — raportul spune exact ce a intrat și ce nu.
    /// </summary>
    public OperationResult<ImportOutcome> ImportCustomers(
        string path, AppUser actor, bool grantWelcomeBonus = false, bool grantPointsColumn = true)
    {
        if (!actor.CanConfigureProgram)
            return OperationResult<ImportOutcome>.Fail("Doar administratorul poate importa clienți.");

        CsvFile.ReadResult file;

        try
        {
            file = CsvFile.Read(path);
        }
        catch (Exception ex)
        {
            return OperationResult<ImportOutcome>.Fail($"Fișierul nu a putut fi citit: {ex.Message}");
        }

        IReadOnlyList<CustomFieldDefinition> fields;
        using (var uow = _factory.Create())
            fields = uow.CustomFields.GetDefinitions();

        var issues = new List<ImportIssue>();
        var created = 0;
        var skipped = 0;
        var failed = 0;
        var pointsGranted = 0;

        foreach (var row in file.Rows)
        {
            var parsed = ParseRow(row, fields);

            if (parsed.Error is not null)
            {
                issues.Add(new ImportIssue { Line = row.LineNumber, Message = parsed.Error });
                skipped++;
                continue;
            }

            var result = _loyalty.EnrollCustomer(parsed.Customer!, actor, grantWelcomeBonus);

            if (result.Failed)
            {
                issues.Add(new ImportIssue { Line = row.LineNumber, Message = result.Error! });
                failed++;
                continue;
            }

            created++;

            if (grantPointsColumn && parsed.InitialPoints > 0)
            {
                var adjust = _loyalty.AdjustPoints(
                    result.Value!.Id, parsed.InitialPoints, "Sold preluat la import", actor);

                if (adjust.Success)
                    pointsGranted += parsed.InitialPoints;
                else
                    issues.Add(new ImportIssue { Line = row.LineNumber, Message = adjust.Error! });
            }
        }

        using (var uow = _factory.Create())
        {
            uow.Audit.Insert(new AuditEntry
            {
                OccurredAt = _clock.Now,
                UserId = actor.Id,
                UserName = actor.Username,
                Action = "CustomersImported",
                EntityType = "Customer",
                Details = $"{created} clienți importați din {Path.GetFileName(path)}, " +
                          $"{skipped} ignorați, {failed} eșuați, {pointsGranted} puncte preluate.",
            });

            uow.Commit();
        }

        return OperationResult<ImportOutcome>.Ok(new ImportOutcome
        {
            Created = created,
            Skipped = skipped,
            Failed = failed,
            PointsGranted = pointsGranted,
            Issues = issues,
        });
    }

    /// <summary>Scrie un fișier model, cu antetul așteptat și un rând de exemplu.</summary>
    public OperationResult WriteImportTemplate(string path)
    {
        try
        {
            using var uow = _factory.Create();
            var fields = uow.CustomFields.GetDefinitions();

            var headers = KnownColumns.Select(c => c.Canonical).Concat(fields.Select(f => f.Key)).ToList();

            var example = new List<string?>
            {
                "Popescu", "Ioana", "0722123456", "ioana.popescu@exemplu.ro",
                "15.06.1990", string.Empty, "0", "Client preluat din vechiul registru",
            };

            example.AddRange(fields.Select(f => f.Kind == CustomFieldKind.Choice
                ? f.ChoiceList.FirstOrDefault() ?? string.Empty
                : string.Empty));

            CsvFile.Write(path, headers, [example]);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Fișierul model nu a putut fi scris: {ex.Message}");
        }
    }

    // ============================================================== Export

    /// <summary>
    /// Toate datele păstrate despre un client, într-un fișier text care se poate da
    /// clientului: răspunsul la o cerere de acces (GDPR, art. 15). Cuprinde datele
    /// personale, acordul de marketing, câmpurile proprii, soldul, cumpărăturile,
    /// recompensele și istoricul punctelor.
    /// </summary>
    public OperationResult ExportCustomerData(int customerId, string path)
    {
        try
        {
            using var uow = _factory.Create();

            var customer = uow.Customers.GetById(customerId);
            if (customer is null)
                return OperationResult.Fail("Clientul nu a fost găsit.");

            var text = new System.Text.StringBuilder();
            void Line(string label, string? value) => text.AppendLine($"{label,-28}{(string.IsNullOrWhiteSpace(value) ? "-" : value)}");
            void Heading(string title) => text.AppendLine().AppendLine(title.ToUpper(CultureInfo.CurrentCulture)).AppendLine(new string('-', title.Length));

            var rules = LoyaltyRules.Load(uow.Settings);

            text.AppendLine($"Datele păstrate de {rules.StoreName} în programul „{rules.ProgramName}”");
            text.AppendLine($"Generat pe {_clock.Now:dd.MM.yyyy HH:mm}");

            Heading("Date personale");
            Line("Nume", customer.LastName);
            Line("Prenume", customer.FirstName);
            Line("Telefon", customer.Phone);
            Line("E-mail", customer.Email);
            Line("Data nașterii", CsvFile.Date(customer.BirthDate));
            Line("Cod card", customer.CardCode);
            Line("Înscris pe", CsvFile.DateTimeText(customer.EnrolledAt));
            Line("Cont activ", customer.IsActive ? "da" : "nu");
            Line("Observații", customer.Notes);

            Heading("Comunicări comerciale");
            Line("Acord", customer.MarketingConsent ? "da" : "nu");
            Line("Exprimat pe", CsvFile.DateTimeText(customer.ConsentAt));

            var values = uow.CustomFields.GetValues(customer.Id);
            var definitions = uow.CustomFields.GetDefinitions(includeInactive: true);
            if (values.Count > 0)
            {
                Heading("Alte informații");
                foreach (var definition in definitions)
                {
                    if (values.TryGetValue(definition.Id, out var value) && !string.IsNullOrWhiteSpace(value))
                        Line(definition.Label, value);
                }
            }

            Heading("Puncte");
            Line("Sold actual", uow.Points.GetBalance(customer.Id).ToString(CultureInfo.CurrentCulture));

            var purchases = uow.Purchases.GetForCustomer(customer.Id, int.MaxValue);
            Heading($"Cumpărături ({purchases.Count})");
            foreach (var p in purchases)
            {
                var status = p.StatusLabel.Length > 0 ? $" · {p.StatusLabel}" : string.Empty;
                text.AppendLine(
                    $"{CsvFile.DateTimeText(p.OccurredAt)}  bon {p.ReceiptNumber ?? "-"}  " +
                    $"{CsvFile.Money(p.NetAmount)} lei  {p.PointsEarned} puncte{status}");
            }

            var redemptions = uow.Redemptions.GetForCustomer(customer.Id, int.MaxValue);
            Heading($"Recompense ({redemptions.Count})");
            foreach (var r in redemptions)
            {
                text.AppendLine(
                    $"{CsvFile.DateTimeText(r.OccurredAt)}  {r.RewardName}  {r.PointsSpent} puncte  " +
                    $"{CsvFile.Money(r.DiscountValue)} lei{(r.IsVoided ? " · anulată" : string.Empty)}");
            }

            var ledger = uow.Points.GetTransactions(customer.Id, int.MaxValue);
            Heading($"Istoricul punctelor ({ledger.Count})");
            foreach (var t in ledger.OrderBy(t => t.OccurredAt).ThenBy(t => t.Id))
            {
                text.AppendLine(
                    $"{CsvFile.DateTimeText(t.OccurredAt)}  {t.TypeLabel,-24}{t.Points,7:+#;-#;0}  " +
                    $"sold {t.BalanceAfter}  {t.Note}");
            }

            File.WriteAllText(path, text.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Exportul nu a reușit: {ex.Message}");
        }
    }

    public OperationResult<int> ExportCustomers(string path)
    {
        try
        {
            using var uow = _factory.Create();

            var rules = LoyaltyRules.Load(uow.Settings);
            var tiers = uow.Tiers.GetAll();
            var fields = uow.CustomFields.GetDefinitions();
            var now = _clock.Now;

            var headers = new List<string>
            {
                "CodCard", "Nume", "Prenume", "Telefon", "Email", "DataNasterii",
                "Inscris", "Activ", "AcordMarketing", "Nivel", "Puncte",
                $"Calificare ({rules.QualifyingUnit()})", "TotalCheltuit", "NumarCumparari",
                "UltimaCumparare", "Observatii",
            };

            headers.AddRange(fields.Select(f => f.Label));

            var rows = new List<IReadOnlyList<string?>>();

            foreach (var customer in uow.Customers.Search(new CustomerQuery { IncludeInactive = true, Take = int.MaxValue }))
            {
                var since = rules.GetQualifyingPeriodStart(now);

                var qualifying = rules.TierBasis switch
                {
                    TierBasis.PointsEarned => uow.Points.GetPointsEarnedSince(customer.Id, since),
                    TierBasis.PurchaseCount => uow.Purchases.GetPurchaseCountSince(customer.Id, since),
                    _ => uow.Purchases.GetNetSpendSince(customer.Id, since),
                };

                var values = uow.CustomFields.GetValues(customer.Id);

                var row = new List<string?>
                {
                    customer.CardCode,
                    customer.LastName,
                    customer.FirstName,
                    customer.Phone,
                    customer.Email,
                    CsvFile.Date(customer.BirthDate),
                    CsvFile.Date(customer.EnrolledAt),
                    customer.IsActive ? "da" : "nu",
                    customer.MarketingConsent ? "da" : "nu",
                    PointsCalculator.DetermineTier(qualifying, tiers).Name,
                    uow.Points.GetBalance(customer.Id).ToString(CultureInfo.CurrentCulture),
                    CsvFile.Money(qualifying),
                    CsvFile.Money(uow.Purchases.GetLifetimeSpend(customer.Id)),
                    uow.Purchases.GetPurchaseCount(customer.Id).ToString(CultureInfo.CurrentCulture),
                    CsvFile.Date(uow.Purchases.GetLastPurchaseDate(customer.Id)),
                    customer.Notes,
                };

                row.AddRange(fields.Select(f => values.TryGetValue(f.Id, out var v) ? v : string.Empty));
                rows.Add(row);
            }

            CsvFile.Write(path, headers, rows);
            return OperationResult<int>.Ok(rows.Count);
        }
        catch (Exception ex)
        {
            return OperationResult<int>.Fail($"Exportul nu a reușit: {ex.Message}");
        }
    }

    public OperationResult<int> ExportPurchases(string path, DateTime fromInclusive, DateTime toExclusive)
    {
        try
        {
            using var uow = _factory.Create();

            var headers = new[]
            {
                "Data", "Bon", "CodCard", "Client", "Valoare", "Reducere", "Incasat", "Returnat",
                "Puncte", "PuncteRetrase", "Nivel", "Multiplicator", "Campanie", "Operator", "Anulata", "MotivAnulare",
            };

            var rows = new List<IReadOnlyList<string?>>();

            foreach (var purchase in uow.Purchases.GetBetween(fromInclusive, toExclusive))
            {
                var customer = uow.Customers.GetById(purchase.CustomerId);

                rows.Add(new List<string?>
                {
                    CsvFile.DateTimeText(purchase.OccurredAt),
                    purchase.ReceiptNumber,
                    customer?.CardCode,
                    customer?.FullName,
                    CsvFile.Money(purchase.GrossAmount),
                    CsvFile.Money(purchase.DiscountAmount),
                    CsvFile.Money(purchase.NetAmount),
                    CsvFile.Money(purchase.ReturnedAmount),
                    purchase.PointsEarned.ToString(CultureInfo.CurrentCulture),
                    purchase.PointsReturned.ToString(CultureInfo.CurrentCulture),
                    purchase.TierAtPurchase,
                    purchase.MultiplierApplied.ToString("0.##", CultureInfo.CurrentCulture),
                    purchase.CampaignName,
                    purchase.RecordedByUserName,
                    purchase.IsVoided ? "da" : "nu",
                    purchase.VoidReason,
                });
            }

            CsvFile.Write(path, headers, rows);
            return OperationResult<int>.Ok(rows.Count);
        }
        catch (Exception ex)
        {
            return OperationResult<int>.Fail($"Exportul nu a reușit: {ex.Message}");
        }
    }

    public OperationResult<int> ExportPointLedger(string path, DateTime fromInclusive, DateTime toExclusive)
    {
        try
        {
            using var uow = _factory.Create();

            var headers = new[]
            {
                "Data", "CodCard", "Client", "Operatiune", "Puncte", "SoldDupa", "Detalii", "Operator",
            };

            var rows = new List<IReadOnlyList<string?>>();

            foreach (var entry in uow.Points.GetTransactionsBetween(fromInclusive, toExclusive))
            {
                var customer = uow.Customers.GetById(entry.CustomerId);

                rows.Add(new List<string?>
                {
                    CsvFile.DateTimeText(entry.OccurredAt),
                    customer?.CardCode,
                    customer?.FullName,
                    entry.TypeLabel,
                    entry.Points.ToString(CultureInfo.CurrentCulture),
                    entry.BalanceAfter.ToString(CultureInfo.CurrentCulture),
                    entry.Note,
                    entry.UserName,
                });
            }

            CsvFile.Write(path, headers, rows);
            return OperationResult<int>.Ok(rows.Count);
        }
        catch (Exception ex)
        {
            return OperationResult<int>.Fail($"Exportul nu a reușit: {ex.Message}");
        }
    }

    // ============================================================== Interne

    private sealed class ParsedRow
    {
        public Customer? Customer { get; init; }
        public int InitialPoints { get; init; }
        public string? Error { get; init; }
    }

    private ParsedRow ParseRow(CsvFile.Row row, IReadOnlyList<CustomFieldDefinition> fields)
    {
        var lastName = Value(row, "Nume");
        var firstName = Value(row, "Prenume");

        if (string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(firstName))
            return new ParsedRow { Error = "Rândul nu are nici nume, nici prenume." };

        if (string.IsNullOrWhiteSpace(lastName))
            return new ParsedRow { Error = "Lipsește numele." };

        if (string.IsNullOrWhiteSpace(firstName))
            return new ParsedRow { Error = "Lipsește prenumele." };

        var birthText = Value(row, "DataNasterii");
        DateTime? birthDate = null;

        if (!string.IsNullOrWhiteSpace(birthText))
        {
            birthDate = CsvFile.ParseDate(birthText);
            if (birthDate is null)
                return new ParsedRow { Error = $"Data nașterii „{birthText}” nu poate fi citită." };
        }

        var pointsText = Value(row, "Puncte");
        var points = 0;

        if (!string.IsNullOrWhiteSpace(pointsText) &&
            (!int.TryParse(pointsText, NumberStyles.Any, CultureInfo.CurrentCulture, out points) || points < 0))
        {
            return new ParsedRow { Error = $"Valoarea „{pointsText}” din coloana Puncte nu este un număr întreg pozitiv." };
        }

        var customer = new Customer
        {
            LastName = lastName.Trim(),
            FirstName = firstName.Trim(),
            Phone = LoyaltyService.NormalizePhone(Value(row, "Telefon")),
            Email = Value(row, "Email"),
            CardCode = Value(row, "CodCard"),
            Notes = Value(row, "Observatii"),
            BirthDate = birthDate,
            EnrolledAt = _clock.Now,
            IsActive = true,
        };

        if (string.IsNullOrEmpty(customer.Phone))
            customer.Phone = null;

        foreach (var field in fields)
        {
            var raw = FieldValue(row, field);
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var error = field.Validate(raw);
            if (error is not null)
                return new ParsedRow { Error = error };

            customer.CustomValues[field.Id] = raw.Trim();
        }

        return new ParsedRow { Customer = customer, InitialPoints = points };
    }

    /// <summary>Motivul pentru care un client din fișier nu poate fi creat, sau null.</summary>
    private static string? FindDuplicate(
        IUnitOfWork uow, Customer customer, HashSet<string> seenPhones, HashSet<string> seenCards)
    {
        if (!string.IsNullOrEmpty(customer.Phone))
        {
            if (seenPhones.Contains(customer.Phone))
                return $"Telefonul {customer.Phone} apare de mai multe ori în fișier.";

            if (uow.Customers.PhoneExists(customer.Phone))
                return $"Telefonul {customer.Phone} aparține unui client deja înscris.";
        }

        if (!string.IsNullOrWhiteSpace(customer.CardCode))
        {
            if (seenCards.Contains(customer.CardCode))
                return $"Codul de card {customer.CardCode} apare de mai multe ori în fișier.";

            if (uow.Customers.CardCodeExists(customer.CardCode))
                return $"Codul de card {customer.CardCode} este deja folosit.";
        }

        return null;
    }

    private static string Value(CsvFile.Row row, string canonical)
    {
        var entry = KnownColumns.First(c => c.Canonical == canonical);

        foreach (var alias in entry.Aliases)
        {
            if (row.Has(alias))
                return row[alias];
        }

        return string.Empty;
    }

    private static string FieldValue(CsvFile.Row row, CustomFieldDefinition field) =>
        row.Has(field.Key) ? row[field.Key] : row[field.Label];

    private static string? MatchColumn(string header)
    {
        foreach (var (canonical, aliases) in KnownColumns)
        {
            if (aliases.Any(a => string.Equals(a, Simplify(header), StringComparison.OrdinalIgnoreCase)))
                return canonical;
        }

        return null;
    }

    private static bool MatchesField(string header, CustomFieldDefinition field) =>
        string.Equals(Simplify(header), Simplify(field.Key), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Simplify(header), Simplify(field.Label), StringComparison.OrdinalIgnoreCase);

    private static string Simplify(string text) =>
        new(text.Trim().ToLowerInvariant()
            .Replace("ă", "a").Replace("â", "a").Replace("î", "i")
            .Replace("ș", "s").Replace("ț", "t")
            .Where(c => char.IsLetterOrDigit(c)).ToArray());
}
