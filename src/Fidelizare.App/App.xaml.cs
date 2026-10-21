using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Fidelizare.App.Infrastructure;
using Fidelizare.App.Views;
using Fidelizare.Data.Sqlite;

namespace Fidelizare.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplyRomanianCulture();

        // O excepție netratată într-un program de casă nu trebuie să închidă
        // aplicația în tăcere: casierul ar rămâne fără unealtă în mijlocul unei vânzări.
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;

        if (!TryInitializeServices())
        {
            Shutdown(1);
            return;
        }

        if (!EnsureAdministratorExists())
        {
            Shutdown();
            return;
        }

        if (!SignIn())
        {
            Shutdown();
            return;
        }

        // Culoarea de accent se aplică înainte de a se construi fereastra
        // principală, ca ecranele să pornească direct în identitatea magazinului.
        Branding.ApplyAccent(AppServices.Current.Loyalty.GetRules().AccentColorHex);

        RunStartupMaintenance();

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        // Până aici aplicația a rulat cu închidere explicită: ferestrele de
        // configurare inițială și de autentificare sunt, pe rând, singurele
        // deschise, iar comportamentul implicit al WPF — „închide programul când
        // se închide ultima fereastră” — ar fi oprit pornirea chiar în momentul
        // autentificării reușite. De acum există fereastra principală, deci
        // închiderea ei poate însemna din nou închiderea programului.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    /// <summary>
    /// Aplicația vorbește românește: sumele se scriu cu virgulă zecimală, datele
    /// în forma zi.lună.an. Fără linia de mai jos, WPF ar afișa după setările
    /// implicite ale controalelor, care sunt în engleză indiferent de Windows.
    /// </summary>
    private static void ApplyRomanianCulture()
    {
        var culture = new CultureInfo("ro-RO");

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }

    private static bool TryInitializeServices()
    {
        try
        {
            var path = DatabaseLocation.Resolve();
            var services = AppServices.Initialize(path);

            // Prima atingere a bazei de date creează structura și valorile implicite.
            // Se face aici, ca eventualele probleme de disc sau de drepturi să apară
            // acum, cu un mesaj clar, nu mai târziu în mijlocul unei operațiuni.
            services.HasAnyUser();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Baza de date nu a putut fi deschisă.\n\n" +
                $"Locație: {DatabaseLocation.ResolvePathWithoutCreating()}\n\n" +
                $"Detalii: {ex.Message}\n\n" +
                "Verifică dacă folderul există și dacă programul are drept de scriere în el.",
                "Program de fidelizare", MessageBoxButton.OK, MessageBoxImage.Error);

            return false;
        }
    }

    /// <summary>
    /// La prima pornire nu există niciun cont. Programul nu vine cu o parolă
    /// implicită — ar fi aceeași pentru toate magazinele care îl primesc — deci
    /// primul administrator este creat aici, de cel care instalează.
    /// </summary>
    private static bool EnsureAdministratorExists()
    {
        if (AppServices.Current.HasAnyUser())
            return true;

        var setup = new FirstRunWindow();
        return setup.ShowDialog() == true;
    }

    private static bool SignIn()
    {
        Branding.ApplyAccent(AppServices.Current.Loyalty.GetRules().AccentColorHex);

        var login = new LoginWindow();
        return login.ShowDialog() == true;
    }

    /// <summary>
    /// Întreținerea de la pornire: se scot din solduri punctele ajunse la termen.
    /// Se face o dată pe zi, la prima pornire, ca să nu încetinească fiecare deschidere.
    /// </summary>
    private static void RunStartupMaintenance()
    {
        try
        {
            AppServices.Current.Loyalty.RunExpiry(AppServices.Current.CurrentUser);
        }
        catch (Exception ex)
        {
            Dialogs.Warn(
                "Verificarea punctelor expirate nu s-a putut face acum. " +
                "Programul funcționează normal, dar unele solduri pot include puncte deja expirate.\n\n" +
                $"Detalii: {ex.Message}");
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);

        var result = MessageBox.Show(
            "A apărut o eroare neașteptată.\n\n" +
            $"{e.Exception.Message}\n\n" +
            "Ultima operațiune nu a fost salvată. Vrei să continui folosirea programului?",
            "Eroare", MessageBoxButton.YesNo, MessageBoxImage.Error);

        // Tranzacțiile se anulează singure când unitatea de lucru se închide fără
        // confirmare, deci baza de date rămâne coerentă și se poate continua.
        e.Handled = result == MessageBoxResult.Yes;

        if (!e.Handled)
            Shutdown(1);
    }

    private static void OnFatalException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogException(ex);
    }

    /// <summary>Scrie eroarea într-un fișier lângă baza de date, pentru diagnostic ulterior.</summary>
    private static void LogException(Exception exception)
    {
        try
        {
            var folder = Path.GetDirectoryName(AppServices.Current?.DatabasePath ?? AppContext.BaseDirectory);
            if (string.IsNullOrEmpty(folder))
                return;

            Directory.CreateDirectory(folder);

            var line =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception.GetType().Name}: {exception.Message}" +
                Environment.NewLine + exception.StackTrace + Environment.NewLine + new string('-', 70) +
                Environment.NewLine;

            File.AppendAllText(Path.Combine(folder, "erori.log"), line);
        }
        catch (Exception)
        {
            // Dacă nici jurnalul nu se poate scrie, nu mai există nimic de făcut aici.
        }
    }
}
