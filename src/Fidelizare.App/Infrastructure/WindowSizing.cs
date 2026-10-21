using System.Windows;

namespace Fidelizare.App.Infrastructure;

/// <summary>
/// Împiedică ferestrele să ceară mai mult spațiu decât are ecranul.
///
/// O înălțime fixă scrisă în XAML pare rezonabilă pe monitorul pe care a fost
/// scrisă, dar Windows lucrează frecvent cu scalare de 125% sau 150%. La 125%
/// pe un ecran de 1536×864, zona de lucru rămâne echivalentul a 652 de unități
/// logice — mai puțin decât înălțimea multor formulare. Fără limitarea de aici,
/// partea de jos a ferestrei ajunge în afara ecranului, iar butoanele și
/// câmpurile de acolo devin inaccesibile.
///
/// Se folosește împreună cu un ScrollViewer în interiorul ferestrei: limita de
/// mai jos oprește creșterea, iar derularea face conținutul accesibil.
/// </summary>
public static class WindowSizing
{
    /// <summary>Marja lăsată față de marginile zonei de lucru, în unități logice.</summary>
    private const double Margin = 24;

    /// <summary>
    /// Leagă înălțimea și lățimea maximă a ferestrei de zona de lucru a ecranului.
    /// Se apelează din constructorul ferestrei, înainte de afișare.
    /// </summary>
    public static void FitToWorkArea(this Window window)
    {
        Apply(window);

        // Rezoluția sau scalarea se pot schimba cât timp programul rulează —
        // la conectarea unui monitor extern, de exemplu.
        window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        var available = SystemParameters.WorkArea;

        var maxHeight = available.Height - Margin;
        var maxWidth = available.Width - Margin;

        // O fereastră maximizată este dimensionată de Windows exact pe zona de
        // lucru, deci nu are nevoie de limita de aici. Impunându-i-o, ar rămâne o
        // fâșie goală între marginea ei și bara de activități, iar fereastra ar
        // arăta ca și cum nu s-ar fi maximizat complet. Limitele minime se aplică
        // însă în continuare: ele sunt cele care pot cere mai mult decât există.
        var maximized = window.WindowState == WindowState.Maximized;

        // Sub aceste valori ecranul este atât de mic încât limitarea ar face mai
        // mult rău decât bine; se lasă fereastra să se descurce singură.
        if (maxHeight > 300)
        {
            if (!maximized)
                window.MaxHeight = maxHeight;

            // O înălțime minimă mai mare decât ecranul ar anula tot efortul.
            if (window.MinHeight > maxHeight)
                window.MinHeight = maxHeight;
        }

        if (maxWidth > 400)
        {
            if (!maximized)
                window.MaxWidth = maxWidth;

            if (window.MinWidth > maxWidth)
                window.MinWidth = maxWidth;
        }
    }
}
