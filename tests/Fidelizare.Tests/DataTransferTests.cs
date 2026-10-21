using System.Text;
using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>
/// Aducerea clienților dintr-un fișier și scoaterea datelor din program.
/// Sunt drumurile pe care un magazin intră în program și, la nevoie, iese din el.
/// </summary>
public class DataTransferTests
{
    private static void WriteCsv(string path, params string[] lines) =>
        File.WriteAllText(path, string.Join(Environment.NewLine, lines), new UTF8Encoding(true));

    [Fact]
    public void Importa_clientii_dintr_un_fisier_simplu()
    {
        using var env = new TestEnvironment();
        var path = env.TempFile("clienti.csv");

        WriteCsv(path,
            "Nume;Prenume;Telefon;Email;DataNasterii;Puncte",
            "Popescu;Ioana;0722111222;ioana@exemplu.ro;15.06.1990;120",
            "Țăranu;Gheorghiță;0733222333;;01.01.1985;0");

        var preview = env.Transfer.PreviewCustomers(path);
        Assert.True(preview.Success, preview.Error);
        Assert.Equal(2, preview.Value!.WillCreate);
        Assert.Equal(0, preview.Value.WillSkip);

        var result = env.Transfer.ImportCustomers(path, env.Admin);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.Value!.Created);
        Assert.Equal(120, result.Value.PointsGranted);

        var found = env.Loyalty.FindByCardOrPhone("0722111222");
        Assert.NotNull(found);
        Assert.Equal("Popescu", found.LastName);
        Assert.Equal(120, env.Balance(found.Id));
    }

    [Fact]
    public void Importul_nu_acorda_bonusul_de_bun_venit_implicit()
    {
        using var env = new TestEnvironment();
        var path = env.TempFile("clienti.csv");

        WriteCsv(path, "Nume;Prenume;Telefon", "Popescu;Ioana;0722111222");

        env.Transfer.ImportCustomers(path, env.Admin);

        var found = env.Loyalty.FindByCardOrPhone("0722111222")!;

        // Clientul exista deja înainte de program: un bonus de înscriere acordat
        // acum ar fi o invenție care i-ar umfla soldul.
        Assert.Equal(0, env.Balance(found.Id));
    }

    [Fact]
    public void Randurile_gresite_sunt_raportate_fara_sa_opreasca_importul()
    {
        using var env = new TestEnvironment();
        var path = env.TempFile("clienti.csv");

        WriteCsv(path,
            "Nume;Prenume;Telefon;DataNasterii",
            "Popescu;Ioana;0722111222;15.06.1990",
            ";FaraNume;0733000000;",
            "Ionescu;Vlad;0744000000;data gresita",
            "Marin;Ana;0755000000;");

        var preview = env.Transfer.PreviewCustomers(path);

        Assert.Equal(4, preview.Value!.TotalRows);
        Assert.Equal(2, preview.Value.WillCreate);
        Assert.Equal(2, preview.Value.WillSkip);
        Assert.Contains(preview.Value.Issues, i => i.Line == 3);
        Assert.Contains(preview.Value.Issues, i => i.Line == 4);

        var result = env.Transfer.ImportCustomers(path, env.Admin);
        Assert.Equal(2, result.Value!.Created);
        Assert.Equal(2, result.Value.Skipped);
    }

    [Fact]
    public void Telefoanele_duplicate_sunt_respinse_inclusiv_in_interiorul_fisierului()
    {
        using var env = new TestEnvironment();
        env.Enroll(phone: "0722111222");

        var path = env.TempFile("clienti.csv");
        WriteCsv(path,
            "Nume;Prenume;Telefon",
            "Duplicat;Extern;0722111222",
            "Unu;Client;0733000000",
            "Doi;Client;0733000000");

        var preview = env.Transfer.PreviewCustomers(path);

        Assert.Equal(1, preview.Value!.WillCreate);
        Assert.Equal(2, preview.Value.WillSkip);
        Assert.Contains(preview.Value.Issues, i => i.Message.Contains("deja înscris"));
        Assert.Contains(preview.Value.Issues, i => i.Message.Contains("de mai multe ori"));
    }

    [Fact]
    public void Separatorul_si_formatul_datei_sunt_recunoscute_automat()
    {
        using var env = new TestEnvironment();
        var path = env.TempFile("clienti.csv");

        // Virgulă ca separator și dată în format ISO — cum vine dintr-un export străin.
        WriteCsv(path, "Nume,Prenume,Telefon,DataNasterii", "Popescu,Ioana,0722111222,1990-06-15");

        var result = env.Transfer.ImportCustomers(path, env.Admin);

        Assert.Equal(1, result.Value!.Created);
        Assert.Equal(new DateTime(1990, 6, 15), env.Loyalty.FindByCardOrPhone("0722111222")!.BirthDate);
    }

    [Fact]
    public void Campurile_proprii_se_importa_dupa_numele_lor_scurt()
    {
        using var env = new TestEnvironment();

        int oras;
        using (var uow = env.Factory.Create())
        {
            oras = uow.CustomFields.InsertDefinition(new CustomFieldDefinition
            {
                Label = "Oraș",
                Key = "oras",
                Kind = CustomFieldKind.Text,
            });

            uow.Commit();
        }

        var path = env.TempFile("clienti.csv");
        WriteCsv(path, "Nume;Prenume;Telefon;oras", "Popescu;Ioana;0722111222;Brașov");

        var result = env.Transfer.ImportCustomers(path, env.Admin);
        Assert.Equal(1, result.Value!.Created);

        var customer = env.Loyalty.GetCustomer(env.Loyalty.FindByCardOrPhone("0722111222")!.Id)!;
        Assert.Equal("Brașov", customer.CustomValues[oras]);
    }

    [Fact]
    public void Exportul_scrie_toti_clientii_cu_nivel_si_puncte()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();
        env.Buy(customer.Id, 1500m);

        var path = env.TempFile("export.csv");
        var result = env.Transfer.ExportCustomers(path);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.Value);

        var written = CsvFile.Read(path);
        Assert.Single(written.Rows);
        Assert.Equal("Popescu", written.Rows[0]["Nume"]);
        Assert.Equal("Silver", written.Rows[0]["Nivel"]);
        Assert.Equal("200", written.Rows[0]["Puncte"]);
    }

    [Fact]
    public void Exportul_si_importul_se_potrivesc_intre_ele()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fidelizare-rt-{Guid.NewGuid():N}.csv");

        try
        {
            using (var sursa = new TestEnvironment())
            {
                sursa.Enroll("Popescu", "Ioana", "0722111222");
                sursa.Enroll("Ionescu", "Vlad", "0733222333");

                Assert.True(sursa.Transfer.ExportCustomers(path).Success);
            }

            // Fișierul exportat dintr-un magazin trebuie să poată fi importat în altul.
            using var destinatie = new TestEnvironment();
            var result = destinatie.Transfer.ImportCustomers(path, destinatie.Admin);

            Assert.True(result.Success, result.Error);
            Assert.Equal(2, result.Value!.Created);
            Assert.Empty(result.Value.Issues);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Fisierul_model_contine_antetul_asteptat()
    {
        using var env = new TestEnvironment();
        var path = env.TempFile("model.csv");

        Assert.True(env.Transfer.WriteImportTemplate(path).Success);

        var read = CsvFile.Read(path);
        Assert.Contains("Nume", read.Headers);
        Assert.Contains("Prenume", read.Headers);
        Assert.Contains("Puncte", read.Headers);
        Assert.Single(read.Rows);
    }

    [Fact]
    public void Doar_administratorul_poate_importa()
    {
        using var env = new TestEnvironment();
        var path = env.TempFile("clienti.csv");
        WriteCsv(path, "Nume;Prenume", "Popescu;Ioana");

        var result = env.Transfer.ImportCustomers(path, env.Cashier);

        Assert.False(result.Success);
    }

    [Fact]
    public void Exportul_cumpararilor_include_campania_aplicata()
    {
        using var env = new TestEnvironment();
        var customer = env.Enroll();

        using (var uow = env.Factory.Create())
        {
            uow.Campaigns.Insert(new Campaign
            {
                Name = "Puncte duble",
                StartsOn = env.Clock.Today,
                EndsOn = env.Clock.Today,
                Multiplier = 2m,
                IsActive = true,
            });

            uow.Commit();
        }

        env.Buy(customer.Id, 300m, receipt: "BON-1");

        var path = env.TempFile("cumparari.csv");
        var result = env.Transfer.ExportPurchases(path, env.Clock.Today, env.Clock.Today.AddDays(1));

        Assert.True(result.Success, result.Error);

        var written = CsvFile.Read(path);
        Assert.Single(written.Rows);
        Assert.Equal("Puncte duble", written.Rows[0]["Campanie"]);
        Assert.Equal("60", written.Rows[0]["Puncte"]);
    }
}
