using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Sqlite;

/// <summary>
/// Copiile de siguranță ale bazei de date.
///
/// Copia se face prin mecanismul de backup al SQLite, nu prin copierea fișierului.
/// În modul WAL, ultimele scrieri pot sta încă în fișierul „-wal”, iar o copiere
/// simplă ar lua doar fișierul principal, adică o bază fără ultimele vânzări.
/// Backup-ul SQLite citește baza așa cum o vede o conexiune, deci copia este
/// completă și coerentă chiar dacă programul lucrează în acel moment.
/// </summary>
public static class DatabaseBackup
{
    /// <summary>Folderul copiilor automate, lângă baza de date.</summary>
    public const string AutomaticFolderName = "copii";

    private const string DailyPrefix = "fidelizare-";
    private const string DailyDateFormat = "yyyy-MM-dd";

    /// <summary>Folderul în care stau copiile automate pentru baza de date dată.</summary>
    public static string GetAutomaticFolder(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".", AutomaticFolderName);

    /// <summary>
    /// Copiază baza de date în fișierul indicat. Se scrie întâi un fișier temporar,
    /// redenumit la final, ca o copie întreruptă să nu înlocuiască una bună.
    /// </summary>
    public static void Create(string databasePath, string destinationPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        var temporary = destinationPath + ".tmp";
        DeleteIfExists(temporary);

        try
        {
            var sourceText = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();

            var destinationText = new SqliteConnectionStringBuilder
            {
                DataSource = temporary,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString();

            using (var source = new SqliteConnection(sourceText))
            using (var destination = new SqliteConnection(destinationText))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);

                // Copia trebuie să fie un singur fișier, ușor de mutat pe o memorie
                // externă. Fără trecerea pe jurnalul clasic, ar putea rămâne cu un
                // „-wal” alături.
                using var journal = destination.CreateCommand();
                journal.CommandText = "PRAGMA journal_mode = DELETE;";
                journal.ExecuteNonQuery();
            }

            File.Move(temporary, destinationPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporary);
        }
    }

    /// <summary>
    /// Copia zilnică: un fișier pe zi, „fidelizare-2026-10-26.db”. O nouă copie în
    /// aceeași zi o înlocuiește pe cea de dimineață, deci folderul nu crește cu
    /// fiecare pornire. Copiile mai vechi decât numărul de zile păstrate se șterg.
    /// </summary>
    /// <returns>Calea copiei create.</returns>
    public static string CreateDaily(string databasePath, string folder, DateTime today, int keepDays)
    {
        var path = Path.Combine(folder, DailyFileName(today));
        Create(databasePath, path);
        Prune(folder, keepDays);
        return path;
    }

    /// <summary>Există deja copia zilei date în folder?</summary>
    public static bool HasDaily(string folder, DateTime day) =>
        File.Exists(Path.Combine(folder, DailyFileName(day)));

    /// <summary>Copiile zilnice din folder, de la cea mai nouă la cea mai veche.</summary>
    public static IReadOnlyList<(DateTime Day, string Path)> ListDaily(string folder)
    {
        if (!Directory.Exists(folder))
            return [];

        var result = new List<(DateTime, string)>();

        foreach (var file in Directory.EnumerateFiles(folder, DailyPrefix + "*.db"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var datePart = name[DailyPrefix.Length..];

            if (DateTime.TryParseExact(datePart, DailyDateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day))
            {
                result.Add((day, file));
            }
        }

        return result.OrderByDescending(r => r.Item1).ToList();
    }

    /// <summary>
    /// Păstrează doar cele mai noi copii zilnice. Se șterg numai fișierele cu numele
    /// copiilor automate, deci o copie manuală pusă în același folder rămâne.
    /// </summary>
    public static int Prune(string folder, int keepDays)
    {
        var removed = 0;

        foreach (var (_, path) in ListDaily(folder).Skip(Math.Max(1, keepDays)))
        {
            try
            {
                File.Delete(path);
                removed++;
            }
            catch (IOException)
            {
                // O copie deschisă în alt program se încearcă din nou data viitoare.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    private static string DailyFileName(DateTime day) =>
        DailyPrefix + day.ToString(DailyDateFormat, CultureInfo.InvariantCulture) + ".db";

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
