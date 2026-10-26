using System.IO;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;
using Fidelizare.Data.Sqlite;

namespace Fidelizare.App.Infrastructure;

/// <summary>
/// Copiile de siguranță automate: una pe zi, lângă baza de date, și opțional
/// într-un al doilea folder.
///
/// Copia se face la pornire, dacă azi nu există încă una, și din nou la
/// închidere, ca să prindă și vânzările zilei. A doua o înlocuiește pe prima,
/// deci rămâne câte un fișier pe zi.
/// </summary>
public static class AutoBackup
{
    public static BackupSettings LoadSettings()
    {
        using var uow = AppServices.Current.Factory.Create();
        return BackupSettings.Load(uow.Settings);
    }

    public static OperationResult SaveSettings(BackupSettings settings, AppUser actor)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
            return OperationResult.Fail(string.Join(Environment.NewLine, errors));

        using var uow = AppServices.Current.Factory.Create();
        settings.Save(uow.Settings);

        uow.Audit.Insert(new AuditEntry
        {
            OccurredAt = AppServices.Current.Clock.Now,
            UserId = actor.Id,
            UserName = actor.Username,
            Action = "BackupSettingsChanged",
            EntityType = "Settings",
            Details = $"Copie automată: {(settings.Enabled ? "pornită" : "oprită")}, " +
                      $"{settings.KeepDays} zile, folder suplimentar: {settings.ExtraFolder ?? "-"}",
        });

        uow.Commit();
        return OperationResult.Ok();
    }

    /// <summary>Folderul principal al copiilor automate, lângă baza de date.</summary>
    public static string MainFolder => DatabaseBackup.GetAutomaticFolder(AppServices.Current.DatabasePath);

    /// <summary>La pornire: copia zilei, doar dacă nu există deja.</summary>
    public static OperationResult RunIfDue()
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
            return OperationResult.Ok();

        var today = AppServices.Current.Clock.Today;
        if (DatabaseBackup.HasDaily(MainFolder, today))
            return OperationResult.Ok();

        return Run(settings);
    }

    /// <summary>La închidere: copia zilei se reface, cu tot ce s-a lucrat azi.</summary>
    public static OperationResult RunOnExit()
    {
        var settings = LoadSettings();
        return settings.Enabled ? Run(settings) : OperationResult.Ok();
    }

    /// <summary>La cerere, din Administrare: copia zilei, chiar dacă cele automate sunt oprite.</summary>
    public static OperationResult RunNow() => Run(LoadSettings());

    private static OperationResult Run(BackupSettings settings)
    {
        var services = AppServices.Current;
        var today = services.Clock.Today;

        try
        {
            var created = DatabaseBackup.CreateDaily(services.DatabasePath, MainFolder, today, settings.KeepDays);

            string? extraError = null;
            if (settings.ExtraFolder is { } extra)
            {
                // Al doilea folder poate fi o memorie externă scoasă din calculator.
                // Lipsa lui nu trebuie să strice copia principală, care tocmai a reușit.
                try
                {
                    Directory.CreateDirectory(extra);
                    File.Copy(created, Path.Combine(extra, Path.GetFileName(created)), overwrite: true);
                    DatabaseBackup.Prune(extra, settings.KeepDays);
                }
                catch (Exception ex)
                {
                    extraError = $"Copia din folderul suplimentar ({extra}) nu a reușit: {ex.Message}";
                }
            }

            using (var uow = services.Factory.Create())
            {
                uow.Settings.Set(BackupSettings.KeyLastBackup, services.Clock.Now.ToString("O"));
                uow.Commit();
            }

            return extraError is null ? OperationResult.Ok() : OperationResult.Fail(extraError);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Copia de siguranță automată nu a reușit: {ex.Message}");
        }
    }
}
