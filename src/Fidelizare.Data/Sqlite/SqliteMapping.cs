using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Sqlite;

/// <summary>
/// Convențiile de stocare, adunate într-un singur loc.
///
/// Sumele de bani se păstrează ca numere întregi de bani, nu ca zecimale sau
/// virgulă mobilă. Motivul este practic: SQLite nu are tip zecimal, iar dacă
/// sumele ar fi stocate ca REAL, un total de tipul SUM(valoare) ar aduna erori
/// de rotunjire care se văd în rapoartele de sfârșit de lună. Cu bani întregi,
/// adunarea este exactă indiferent câte rânduri sunt.
///
/// Datele calendaristice se scriu în format ISO, care se sortează corect ca text,
/// deci comparațiile din SQL funcționează fără conversii.
/// </summary>
internal static class Db
{
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
    public const string DateFormat = "yyyy-MM-dd";

    // --------------------------------------------------------------- Bani

    /// <summary>Lei → bani. Rotunjirea se face la jumătate în sus, ca la casă.</summary>
    public static long ToBani(decimal lei) =>
        (long)Math.Round(lei * 100m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromBani(long bani) => bani / 100m;

    /// <summary>Multiplicatorii și procentele se păstrează în miimi, ca 1,25 să fie exact 1250.</summary>
    public static long ToMilli(decimal value) =>
        (long)Math.Round(value * 1000m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromMilli(long milli) => milli / 1000m;

    // ---------------------------------------------------------------- Timp

    public static string ToText(DateTime value) =>
        value.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    public static string ToDateText(DateTime value) =>
        value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static object ToTextOrNull(DateTime? value) =>
        value.HasValue ? ToText(value.Value) : DBNull.Value;

    public static object ToDateTextOrNull(DateTime? value) =>
        value.HasValue ? ToDateText(value.Value) : DBNull.Value;

    public static DateTime ParseTimestamp(string text)
    {
        if (DateTime.TryParseExact(text, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var value))
            return value;

        // Toleranță pentru valori scrise de versiuni anterioare sau importate manual.
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? fallback
            : DateTime.MinValue;
    }

    public static DateTime ParseDate(string text) => ParseTimestamp(text).Date;

    // ------------------------------------------------------------ Parametri

    public static SqliteParameter With(this SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }
}

/// <summary>Citire tolerantă la valori lipsă, pe nume de coloană.</summary>
internal static class ReaderExtensions
{
    public static string GetText(this SqliteDataReader reader, string column) =>
        reader.GetString(reader.GetOrdinal(column));

    public static string? GetTextOrNull(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int GetInt(this SqliteDataReader reader, string column) =>
        reader.GetInt32(reader.GetOrdinal(column));

    public static int? GetIntOrNull(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static long GetLong(this SqliteDataReader reader, string column) =>
        reader.GetInt64(reader.GetOrdinal(column));

    public static bool GetBool(this SqliteDataReader reader, string column) =>
        reader.GetInt64(reader.GetOrdinal(column)) != 0;

    public static decimal GetMoney(this SqliteDataReader reader, string column) =>
        Db.FromBani(reader.GetInt64(reader.GetOrdinal(column)));

    public static decimal? GetMoneyOrNull(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Db.FromBani(reader.GetInt64(ordinal));
    }

    public static decimal GetMilli(this SqliteDataReader reader, string column) =>
        Db.FromMilli(reader.GetInt64(reader.GetOrdinal(column)));

    public static DateTime GetTimestamp(this SqliteDataReader reader, string column) =>
        Db.ParseTimestamp(reader.GetString(reader.GetOrdinal(column)));

    public static DateTime? GetTimestampOrNull(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Db.ParseTimestamp(reader.GetString(ordinal));
    }

    public static DateTime? GetDateOrNull(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Db.ParseDate(reader.GetString(ordinal));
    }
}
