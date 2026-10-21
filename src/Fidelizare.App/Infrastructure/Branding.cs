using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fidelizare.Core.Services;

namespace Fidelizare.App.Infrastructure;

/// <summary>
/// Identitatea vizuală a magazinului: culoarea de accent și logo-ul.
///
/// Logo-ul se păstrează în baza de date, nu ca fișier alături de program. Astfel
/// copia de siguranță conține tot, iar mutarea programului pe alt calculator nu
/// lasă în urmă o imagine lipsă.
/// </summary>
public static class Branding
{
    public const string LogoSettingKey = "brand.logo";

    /// <summary>Peste această dimensiune imaginea este refuzată: un logo nu are nevoie de mai mult.</summary>
    private const int MaxLogoBytes = 512 * 1024;

    /// <summary>
    /// Aplică culoarea de accent asupra pensulelor din temă.
    ///
    /// Resursele se înlocuiesc cu pensule noi. Ecranele le referă prin
    /// DynamicResource tocmai pentru asta: o referință statică s-ar fi rezolvat o
    /// singură dată, la încărcarea ferestrei, iar schimbarea culorii nu s-ar fi
    /// văzut decât după repornirea programului.
    /// </summary>
    public static void ApplyAccent(string hex)
    {
        if (!LoyaltyRules.IsValidHexColor(hex))
            return;

        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        var accent = (Color)ColorConverter.ConvertFromString(hex);

        Set(resources, "Accent", accent);
        Set(resources, "AccentDark", Darken(accent, 0.78));
        Set(resources, "AccentSoft", Tint(accent, 0.90));
    }

    /// <summary>Logo-ul salvat, gata de afișat, sau null dacă magazinul nu a pus niciunul.</summary>
    public static BitmapImage? LoadLogo()
    {
        var bytes = LoadLogoBytes();
        if (bytes is null)
            return null;

        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);

            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception)
        {
            // O imagine deteriorată nu trebuie să împiedice pornirea programului.
            return null;
        }
    }

    public static byte[]? LoadLogoBytes()
    {
        try
        {
            using var uow = AppServices.Current.Factory.Create();
            var stored = uow.Settings.Get(LogoSettingKey);

            return string.IsNullOrWhiteSpace(stored) ? null : Convert.FromBase64String(stored);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Salvează sau șterge logo-ul. Fișierul dat trebuie să fie o imagine.</summary>
    public static OperationResult SaveLogo(string? imagePath)
    {
        try
        {
            string value;

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                value = string.Empty;
            }
            else
            {
                var bytes = File.ReadAllBytes(imagePath);

                if (bytes.Length > MaxLogoBytes)
                {
                    return OperationResult.Fail(
                        $"Imaginea are {bytes.Length / 1024} KB. Alege una sub {MaxLogoBytes / 1024} KB — " +
                        "un logo de bară laterală nu are nevoie de mai mult.");
                }

                // Se încarcă o dată aici, ca un fișier care nu este imagine să fie
                // respins acum, nu la următoarea pornire a programului.
                using var stream = new MemoryStream(bytes);
                var probe = new BitmapImage();
                probe.BeginInit();
                probe.CacheOption = BitmapCacheOption.OnLoad;
                probe.StreamSource = stream;
                probe.EndInit();

                value = Convert.ToBase64String(bytes);
            }

            using var uow = AppServices.Current.Factory.Create();
            uow.Settings.Set(LogoSettingKey, value);
            uow.Commit();

            return OperationResult.Ok();
        }
        catch (NotSupportedException)
        {
            return OperationResult.Fail("Fișierul ales nu este o imagine pe care programul să o poată afișa.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Logo-ul nu a putut fi salvat: {ex.Message}");
        }
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        resources[key] = brush;
    }

    private static Color Darken(Color color, double factor) => Color.FromRgb(
        (byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));

    /// <summary>Amestecă culoarea cu alb, pentru fundalurile discrete.</summary>
    private static Color Tint(Color color, double towardsWhite) => Color.FromRgb(
        (byte)(color.R + (255 - color.R) * towardsWhite),
        (byte)(color.G + (255 - color.G) * towardsWhite),
        (byte)(color.B + (255 - color.B) * towardsWhite));
}
