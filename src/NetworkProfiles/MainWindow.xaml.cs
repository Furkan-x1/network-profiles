using System.Windows;
using NetworkProfiles.Services;
namespace NetworkProfiles;
public partial class MainWindow : Window
{
    private readonly AdapterDiscoveryService _discovery = new();
    public MainWindow() { InitializeComponent(); RefreshAdapters(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAdapters();
    private void RefreshAdapters()
    {
        try {
            var adapters = _discovery.GetAdapters();
            AdaptersGrid.ItemsSource = adapters;
            StatusText.Text = $"{adapters.Count} adaptör bulundu. Salt okunur mod; ayarlar değiştirilmez.";
        } catch (Exception ex) { StatusText.Text = $"Adaptör bilgileri okunamadı: {ex.Message}"; }
    }
}
