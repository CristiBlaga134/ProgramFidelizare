using Fidelizare.Core.Services;
using Fidelizare.Data.Sqlite;

namespace Fidelizare.Tests;

/// <summary>Copiile de siguranță: conținutul lor, câte una pe zi și ștergerea celor vechi.</summary>
public class BackupTests
{
    [Fact]
    public void Copia_contine_ultimele_vanzari_si_se_deschide_ca_baza_de_date()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        env.Buy(customer.Id, 250m);

        var copy = env.TempFile("copie.db");
        DatabaseBackup.Create(env.Factory.DatabasePath, copy);

        Assert.True(File.Exists(copy));
        Assert.False(File.Exists(copy + "-wal"));

        var restored = new SqliteUnitOfWorkFactory(copy);
        using var uow = restored.Create();
        Assert.Single(uow.Purchases.GetForCustomer(customer.Id));
        Assert.Equal(75, uow.Points.GetBalance(customer.Id));
    }

    [Fact]
    public void Copia_zilnica_se_inlocuieste_in_aceeasi_zi()
    {
        using var env = new TestEnvironment();
        var folder = env.TempFile("copii");
        var day = new DateTime(2026, 10, 26);

        DatabaseBackup.CreateDaily(env.Factory.DatabasePath, folder, day, keepDays: 30);
        env.Enroll();
        DatabaseBackup.CreateDaily(env.Factory.DatabasePath, folder, day, keepDays: 30);

        var copies = DatabaseBackup.ListDaily(folder);
        Assert.Single(copies);
        Assert.True(DatabaseBackup.HasDaily(folder, day));
        Assert.EndsWith("fidelizare-2026-10-26.db", copies[0].Path);
    }

    [Fact]
    public void Copiile_mai_vechi_decat_numarul_de_zile_se_sterg_iar_cele_manuale_raman()
    {
        using var env = new TestEnvironment();
        var folder = env.TempFile("copii");
        var start = new DateTime(2026, 10, 1);

        for (var i = 0; i < 5; i++)
            DatabaseBackup.CreateDaily(env.Factory.DatabasePath, folder, start.AddDays(i), keepDays: 30);

        var manual = Path.Combine(folder, "fidelizare-manual-2026-10-01-1200.db");
        File.WriteAllText(manual, "copie manuală");

        DatabaseBackup.Prune(folder, keepDays: 3);

        var kept = DatabaseBackup.ListDaily(folder).Select(c => c.Day).ToList();
        Assert.Equal([start.AddDays(4), start.AddDays(3), start.AddDays(2)], kept);
        Assert.True(File.Exists(manual));
    }

    [Fact]
    public void Setarile_copiilor_se_salveaza_si_au_valori_implicite_sigure()
    {
        using var env = new TestEnvironment();

        using (var uow = env.Factory.Create())
        {
            var defaults = BackupSettings.Load(uow.Settings);
            Assert.True(defaults.Enabled);
            Assert.Equal(30, defaults.KeepDays);
            Assert.Null(defaults.ExtraFolder);

            new BackupSettings { Enabled = false, KeepDays = 7, ExtraFolder = @"E:\copii" }.Save(uow.Settings);
            uow.Commit();
        }

        using (var uow = env.Factory.Create())
        {
            var saved = BackupSettings.Load(uow.Settings);
            Assert.False(saved.Enabled);
            Assert.Equal(7, saved.KeepDays);
            Assert.Equal(@"E:\copii", saved.ExtraFolder);
        }

        Assert.NotEmpty(new BackupSettings { KeepDays = 0 }.Validate());
    }
}
