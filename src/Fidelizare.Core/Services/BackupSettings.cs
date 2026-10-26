using System.Globalization;
using Fidelizare.Core.Abstractions;

namespace Fidelizare.Core.Services;

/// <summary>
/// Setările copiilor de siguranță automate.
///
/// Toate datele magazinului stau într-un singur fișier, deci un disc defect sau
/// un fișier șters din greșeală ar însemna pierderea tuturor clienților. Copia
/// manuală există, dar depinde de cineva care să își amintească de ea; copia
/// automată nu.
/// </summary>
public sealed class BackupSettings
{
    public const string KeyEnabled = "backup.auto";
    public const string KeyExtraFolder = "backup.extra_folder";
    public const string KeyKeepDays = "backup.keep_days";
    public const string KeyLastBackup = "system.last_backup";

    /// <summary>Copie automată zilnică. Pornită implicit: o bază fără copii este riscul cel mai mare.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Un al doilea loc pentru copii, opțional: o memorie externă sau un folder
    /// sincronizat. Copiile de lângă baza de date nu ajută dacă se strică discul.
    /// </summary>
    public string? ExtraFolder { get; set; }

    /// <summary>Câte zile de copii se păstrează. Cele mai vechi se șterg.</summary>
    public int KeepDays { get; set; } = 30;

    /// <summary>Momentul ultimei copii automate reușite.</summary>
    public DateTime? LastBackupAt { get; set; }

    public static BackupSettings Load(ISettingsRepository settings)
    {
        var result = new BackupSettings();

        if (settings.Get(KeyEnabled) is { } enabled)
            result.Enabled = enabled != "0";

        var extra = settings.Get(KeyExtraFolder);
        result.ExtraFolder = string.IsNullOrWhiteSpace(extra) ? null : extra;

        if (int.TryParse(settings.Get(KeyKeepDays), NumberStyles.Integer, CultureInfo.InvariantCulture, out var keep))
            result.KeepDays = keep;

        if (DateTime.TryParse(settings.Get(KeyLastBackup), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last))
            result.LastBackupAt = last;

        return result;
    }

    public void Save(ISettingsRepository settings)
    {
        settings.Set(KeyEnabled, Enabled ? "1" : "0");
        settings.Set(KeyExtraFolder, ExtraFolder ?? string.Empty);
        settings.Set(KeyKeepDays, KeepDays.ToString(CultureInfo.InvariantCulture));
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (KeepDays is < 1 or > 365)
            errors.Add("Numărul de zile păstrate trebuie să fie între 1 și 365.");

        return errors;
    }
}
