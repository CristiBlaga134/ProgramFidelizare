using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Fidelizare.App.Infrastructure;
using Fidelizare.App.ViewModels;
using Fidelizare.Core.Abstractions;

namespace Fidelizare.App.Views;

/// <summary>Lista de clienți, cu căutare și acces la fișa completă.</summary>
public partial class CustomersView : UserControl, IRefreshable
{
    private const string SearchHint = "Caută după nume, telefon sau cod de card";

    /// <summary>
    /// Căutarea nu pornește la fiecare tastă: se așteaptă o scurtă pauză, altfel
    /// scrierea unui nume ar declanșa o interogare pentru fiecare literă.
    /// </summary>
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private bool _hintVisible = true;

    public CustomersView()
    {
        InitializeComponent();

        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            Refresh();
        };

        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var services = AppServices.Current;

        var query = new CustomerQuery
        {
            SearchText = _hintVisible ? null : SearchInput.Text,
            IncludeInactive = IncludeInactive.IsChecked == true,
            Take = 500,
        };

        try
        {
            var rows = CustomerRow.Load(services.Factory, services.Clock, query);

            Grid.ItemsSource = rows;
            CountText.Text = rows.Count switch
            {
                0 => "Niciun client găsit.",
                1 => "Un client.",
                _ => $"{rows.Count} clienți.",
            };
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Lista de clienți nu a putut fi încărcată: {ex.Message}");
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_hintVisible)
            return;

        _searchDelay.Stop();
        _searchDelay.Start();
    }

    /// <summary>Textul de îndrumare dispare la prima atingere a câmpului.</summary>
    private void OnSearchGotFocus(object sender, RoutedEventArgs e)
    {
        if (!_hintVisible)
            return;

        _hintVisible = false;
        SearchInput.Text = string.Empty;
        SearchInput.Foreground = (System.Windows.Media.Brush)FindResource("Text");
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnNew(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomerWindow { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            Refresh();
    }

    private void OnOpen(object sender, RoutedEventArgs e) => OpenSelected();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (Grid.SelectedItem is not CustomerRow row)
        {
            Dialogs.Info("Selectează un client din listă.");
            return;
        }

        var dialog = new CustomerDetailWindow(row.Customer.Id) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();

        if (dialog.DataChanged)
            Refresh();
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not CustomerRow row)
        {
            Dialogs.Info("Selectează un client din listă.");
            return;
        }

        var dialog = new CustomerWindow(row.Customer) { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() == true)
            Refresh();
    }
}
