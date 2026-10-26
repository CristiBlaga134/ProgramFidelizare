using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Services;
using Fidelizare.Data.Sqlite;
using Microsoft.Win32;

namespace Fidelizare.App.Views;

/// <summary>Fila Sistem: copiile de siguranță, manuale și automate.</summary>
public partial class AdminView
{
    private string? _extraBackupFolder;

    private void LoadBackup()
    {
        var settings = AutoBackup.LoadSettings();

        AutoBackupCheck.IsChecked = settings.Enabled;
        BackupKeepDaysBox.Text = settings.KeepDays.ToString(CultureInfo.CurrentCulture);
        BackupFolderBox.Text = AutoBackup.MainFolder;

        _extraBackupFolder = settings.ExtraFolder;
        ExtraBackupFolderBox.Text = _extraBackupFolder ?? "Niciunul";

        var copies = DatabaseBackup.ListDaily(AutoBackup.MainFolder);
        LastBackupText.Text = settings.LastBackupAt is { } last
            ? $"Ultima copie automată: {last:dd.MM.yyyy HH:mm}. În folder sunt {copies.Count} copii."
            : "Nu s-a făcut încă nicio copie automată.";
    }

    /// <summary>
    /// Copia manuală, într-un fișier ales. Folosește tot copia SQLite, nu copierea
    /// fișierului, ca să cuprindă și ultimele scrieri aflate încă în jurnalul WAL.
    /// </summary>
    private void OnBackup(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;

        var dialog = new SaveFileDialog
        {
            Title = "Salvează copia de siguranță",
            FileName = $"fidelizare-manual-{services.Clock.Now:yyyy-MM-dd-HHmm}.db",
            Filter = "Bază de date (*.db)|*.db|Toate fișierele (*.*)|*.*",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            DatabaseBackup.Create(services.DatabasePath, dialog.FileName);
            Dialogs.Info($"Copia de siguranță a fost salvată:\n{dialog.FileName}");
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Copia nu a putut fi salvată: {ex.Message}");
        }
    }

    private void OnChooseExtraBackupFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Alege al doilea folder pentru copii",
            InitialDirectory = _extraBackupFolder ?? string.Empty,
        };

        if (dialog.ShowDialog() != true)
            return;

        _extraBackupFolder = dialog.FolderName;
        ExtraBackupFolderBox.Text = _extraBackupFolder;
    }

    private void OnClearExtraBackupFolder(object sender, RoutedEventArgs e)
    {
        _extraBackupFolder = null;
        ExtraBackupFolderBox.Text = "Niciunul";
    }

    private void OnSaveBackupSettings(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BackupKeepDaysBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var keep))
        {
            Dialogs.Warn("Numărul de zile păstrate trebuie să fie un număr întreg.");
            return;
        }

        var settings = new BackupSettings
        {
            Enabled = AutoBackupCheck.IsChecked == true,
            ExtraFolder = _extraBackupFolder,
            KeepDays = keep,
        };

        var result = AutoBackup.SaveSettings(settings, AppServices.Current.RequireUser);

        if (result.Failed)
        {
            Dialogs.Warn(result.Error!);
            return;
        }

        if (!settings.Enabled)
        {
            Dialogs.Warn("Copiile automate sunt oprite. Fără ele, un disc defect înseamnă pierderea tuturor datelor. " +
                         "Fă copii manuale regulat.");
        }
        else
        {
            Dialogs.Info("Setările copiilor de siguranță au fost salvate.");
        }

        LoadBackup();
    }

    private void OnRunBackupNow(object sender, RoutedEventArgs e)
    {
        var result = AutoBackup.RunNow();

        if (result.Failed)
            Dialogs.Warn(result.Error!);
        else
            Dialogs.Info($"Copia de azi a fost făcută în:\n{AutoBackup.MainFolder}");

        LoadBackup();
    }

    private void OnOpenBackupFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AutoBackup.MainFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", AutoBackup.MainFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Folderul nu a putut fi deschis: {ex.Message}");
        }
    }
}
