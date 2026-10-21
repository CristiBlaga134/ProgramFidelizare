using System.Diagnostics;
using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Services;

namespace Fidelizare.Tests;

/// <summary>
/// Generatorul de date demonstrative. Este singura operațiune din program care
/// face mii de scrieri la un singur clic, deci merită verificat că se termină,
/// că produce date coerente și în cât timp — dacă ar dura minute, ar trebui
/// mutat pe alt fir de execuție, ca interfața să nu pară blocată.
/// </summary>
public class DemoDataTests
{
    [Fact]
    public void Genereaza_clienti_cu_istoric_coerent()
    {
        using var env = new TestEnvironment();
        var demo = new DemoDataGenerator(env.Factory, env.Loyalty, env.Clock);

        var watch = Stopwatch.StartNew();
        var result = demo.Generate(env.Admin, customerCount: 60);
        watch.Stop();

        Assert.True(result.Success, result.Error);
        Assert.Equal(60, result.Value);

        using var uow = env.Factory.Create();
        var clienti = uow.Customers.Count(new CustomerQuery { IncludeInactive = true });
        var cumparari = uow.Purchases.GetBetween(DateTime.MinValue, env.Clock.Now.AddDays(1)).Count;

        Assert.Equal(60, clienti);
        Assert.True(cumparari > 100, $"Prea puține cumpărări generate: {cumparari}");

        // Pragul este larg intenționat: pe un calculator lent operațiunea poate
        // dura mai mult, dar dacă trece de un minut înseamnă că ceva s-a stricat.
        Assert.True(watch.Elapsed.TotalSeconds < 60,
            $"Generarea a durat {watch.Elapsed.TotalSeconds:N1} secunde pentru {cumparari} cumpărări.");
    }

    [Fact]
    public void Refuza_generarea_peste_date_existente()
    {
        using var env = new TestEnvironment();
        env.Enroll();

        var demo = new DemoDataGenerator(env.Factory, env.Loyalty, env.Clock);
        var result = demo.Generate(env.Admin, customerCount: 10);

        Assert.False(result.Success);
        Assert.Contains("deja", result.Error!);
    }

    [Fact]
    public void Doar_administratorul_poate_genera()
    {
        using var env = new TestEnvironment();
        var demo = new DemoDataGenerator(env.Factory, env.Loyalty, env.Clock);

        var result = demo.Generate(env.Cashier, customerCount: 10);

        Assert.False(result.Success);
    }
}
