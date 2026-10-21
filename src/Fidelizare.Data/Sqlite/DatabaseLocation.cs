using System.Text;

namespace Fidelizare.Data.Sqlite;

/// <summary>
/// Decide unde stă fișierul bazei de date.
///
/// Programul este portabil: se copiază un executabil și se rulează. Baza de date
/// stă implicit lângă el, într-un subfolder „date”, ca tot ce ține de magazin să
/// poată fi salvat copiind un singur folder.
///
/// Dacă executabilul a fost pus într-un loc unde Windows nu permite scrierea —
/// de obicei Program Files — se trece automat pe folderul de date al utilizatorului,
/// altfel aplicația ar porni și ar crăpa la prima scriere.
///
/// Pentru cazul cu mai multe case de marcat, calea poate fi indicată explicit
/// într-un fișier text lângă executabil, fără recompilare.
/// </summary>
public static class DatabaseLocation
{
    public const string ConfigFileName = "fidelizare.config";
    public const string DatabaseFileName = "fidelizare.db";

    /// <summary>Calea completă a fișierului bazei de date, creând folderul dacă lipsește.</summary>
    public static string Resolve()
    {
        var path = ResolvePathWithoutCreating();
        var folder = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        return path;
    }

    /// <summary>Unde ar sta baza de date, fără a crea nimic pe disc.</summary>
    public static string ResolvePathWithoutCreating()
    {
        var appFolder = AppContext.BaseDirectory;

        var configured = ReadConfiguredPath(Path.Combine(appFolder, ConfigFileName));
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        var localData = Path.Combine(appFolder, "date");
        if (CanWriteTo(appFolder))
            return Path.Combine(localData, DatabaseFileName);

        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProgramFidelizare");

        return Path.Combine(userData, DatabaseFileName);
    }

    /// <summary>
    /// Bazele de date aflate pe un share de rețea nu suportă jurnalul WAL:
    /// blocarea fișierelor peste SMB nu oferă garanțiile de care are nevoie.
    /// În acest caz se folosește jurnalul clasic, mai lent dar corect.
    /// </summary>
    public static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return true;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
                return false;

            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.Network;
        }
        catch (Exception)
        {
            // Dacă nu se poate determina tipul unității, se presupune varianta prudentă.
            return false;
        }
    }

    /// <summary>Citește calea din fișierul de configurare, ignorând comentariile.</summary>
    private static string? ReadConfiguredPath(string configPath)
    {
        if (!File.Exists(configPath))
            return null;

        try
        {
            foreach (var rawLine in File.ReadAllLines(configPath, Encoding.UTF8))
            {
                var line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"');

                if (key.Equals("database", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    return value;
            }
        }
        catch (Exception)
        {
            // Un fișier de configurare deteriorat nu trebuie să împiedice pornirea:
            // se continuă cu locația implicită.
        }

        return null;
    }

    private static bool CanWriteTo(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".scriere_{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
