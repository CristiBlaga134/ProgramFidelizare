using System.Globalization;
using System.Text;

namespace Fidelizare.Core.Services;

/// <summary>
/// Citirea și scrierea fișierelor CSV.
///
/// Formatul este ales pentru Excel-ul românesc: separator punct și virgulă, nu
/// virgulă. Motivul este că în setările regionale românești virgula este separator
/// zecimal, iar un fișier cu virgule ar deschide „1,50 lei” ca două coloane.
/// Fișierele se scriu cu marcaj de ordine a octeților, altfel Excel presupune
/// codificarea locală și strică diacriticele.
/// </summary>
public static class CsvFile
{
    public const char Separator = ';';

    private static readonly UTF8Encoding EncodingWithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Un rând citit din fișier, cu valorile pe numele coloanei.</summary>
    public sealed class Row
    {
        private readonly Dictionary<string, string> _values;

        internal Row(Dictionary<string, string> values, int lineNumber)
        {
            _values = values;
            LineNumber = lineNumber;
        }

        /// <summary>Numărul liniei din fișier, folosit în mesajele de eroare.</summary>
        public int LineNumber { get; }

        /// <summary>Valoarea unei coloane, sau text gol dacă lipsește. Numele nu ține cont de majuscule.</summary>
        public string this[string column] =>
            _values.TryGetValue(Normalize(column), out var value) ? value : string.Empty;

        public bool Has(string column) => _values.ContainsKey(Normalize(column));
    }

    public sealed class ReadResult
    {
        public required IReadOnlyList<string> Headers { get; init; }
        public required IReadOnlyList<Row> Rows { get; init; }
    }

    /// <summary>
    /// Citește un fișier CSV. Acceptă atât punct și virgulă, cât și virgulă sau
    /// tabulator ca separator — fișierele vin de la case de marcat și de la
    /// programe de contabilitate diferite, iar ghicirea separatorului scutește
    /// utilizatorul de o întrebare la care de multe ori nu știe răspunsul.
    /// </summary>
    public static ReadResult Read(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var lines = SplitLines(text);

        if (lines.Count == 0)
            return new ReadResult { Headers = [], Rows = [] };

        var separator = GuessSeparator(lines[0]);
        var headers = ParseLine(lines[0], separator);

        var rows = new List<Row>();

        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var cells = ParseLine(lines[i], separator);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var c = 0; c < headers.Count; c++)
                values[Normalize(headers[c])] = c < cells.Count ? cells[c] : string.Empty;

            rows.Add(new Row(values, i + 1));
        }

        return new ReadResult { Headers = headers, Rows = rows };
    }

    public static void Write(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();

        builder.AppendLine(string.Join(Separator, headers.Select(Escape)));

        foreach (var row in rows)
            builder.AppendLine(string.Join(Separator, row.Select(Escape)));

        File.WriteAllText(path, builder.ToString(), EncodingWithBom);
    }

    /// <summary>Scrie o sumă în forma pe care Excel-ul românesc o citește ca număr.</summary>
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.CurrentCulture);

    public static string Date(DateTime? value) => value?.ToString("dd.MM.yyyy") ?? string.Empty;

    public static string DateTimeText(DateTime? value) => value?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;

    /// <summary>Citește o dată scrisă în oricare dintre formele obișnuite.</summary>
    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string[] formats =
        [
            "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy",
            "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yy",
        ];

        if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exact))
            return exact;

        return DateTime.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var loose)
            ? loose
            : null;
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    /// <summary>Separatorul este cel mai frecvent din linia de antet.</summary>
    private static char GuessSeparator(string header)
    {
        var counts = new Dictionary<char, int>
        {
            [';'] = header.Count(c => c == ';'),
            [','] = header.Count(c => c == ','),
            ['\t'] = header.Count(c => c == '\t'),
        };

        var best = counts.OrderByDescending(p => p.Value).First();
        return best.Value > 0 ? best.Key : Separator;
    }

    /// <summary>Împarte o linie, respectând ghilimelele și ghilimelele dublate din interiorul lor.</summary>
    private static List<string> ParseLine(string line, char separator)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c == '"')
                inQuotes = true;
            else if (c == separator)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
                current.Append(c);
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuotes = value.Contains(Separator) || value.Contains('"') ||
                          value.Contains('\n') || value.Contains('\r');

        return needsQuotes ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }

    /// <summary>Numele coloanelor se compară fără majuscule, spații sau diacritice.</summary>
    private static string Normalize(string header)
    {
        var builder = new StringBuilder(header.Length);

        foreach (var c in header.Trim().ToLowerInvariant())
        {
            builder.Append(c switch
            {
                'ă' or 'â' => 'a',
                'î' => 'i',
                'ș' or 'ş' => 's',
                'ț' or 'ţ' => 't',
                ' ' or '_' or '-' => '\0',
                _ => c,
            });
        }

        return builder.ToString().Replace("\0", string.Empty);
    }
}
