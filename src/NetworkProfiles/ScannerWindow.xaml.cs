using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using NetworkProfiles.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace NetworkProfiles;

public partial class ScannerWindow : Window
{
    private readonly NetworkScannerService _scanner = new();
    private CancellationTokenSource? _cancellation;

    public ScannerWindow(string? suggestedCidr = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(suggestedCidr))
            CidrBox.Text = suggestedCidr;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ResultsGrid.ItemsSource = null;
        var progress = new Progress<int>(count => StatusText.Text = $"Scanning… {count} addresses checked.");
        try
        {
            var results = await _scanner.ScanAsync(CidrBox.Text, progress, _cancellation.Token);
            ResultsGrid.ItemsSource = results;
            StatusText.Text = $"Scan complete. {results.Count} hosts responded.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Could not start scan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.ItemsSource is not IEnumerable<ScanResult> results)
        {
            MessageBox.Show(this, "Run a scan first.", "No results", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export scan results",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "network-scan.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var csv = new StringBuilder("IPv4,RoundtripMs,Status\r\n");
            foreach (var item in results)
                csv.Append(item.Address).Append(',').Append(item.RoundtripTimeMs).Append(",").Append(item.Status).Append("\r\n");
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            StatusText.Text = $"Results exported to: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        base.OnClosed(e);
    }
}