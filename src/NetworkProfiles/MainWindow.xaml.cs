using System.Windows;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using NetworkProfiles.Models;
using NetworkProfiles.Services;

namespace NetworkProfiles;

public partial class MainWindow : Window
{
    private readonly AdapterDiscoveryService _discovery = new();
    private readonly ProfileStore _store = new();
    private List<AdapterStatus> _adapters = [];
    private List<NetworkProfile> _profiles = [];

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            _profiles = _store.Load().ToList();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Kayıtlı profiller okunamadı: {ex.Message}";
        }

        RefreshAdapters();
        RefreshProfiles();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdapters();
        RefreshProfiles();
    }

    private void RefreshAdapters()
    {
        try
        {
            _adapters = _discovery.GetAdapters().ToList();
            AdaptersGrid.ItemsSource = _adapters;
            StatusText.Text = $"{_adapters.Count} adaptör bulundu. Bağlı adaptörler üstte listelenir.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Adaptör bilgileri okunamadı: {ex.Message}";
        }
    }

    private void RefreshProfiles()
    {
        var adapterNames = _adapters.ToDictionary(a => a.Id, a => a.Name, StringComparer.OrdinalIgnoreCase);
        ProfilesGrid.ItemsSource = _profiles.Select(profile => new ProfileRow(
            profile,
            profile.Name,
            adapterNames.TryGetValue(profile.AdapterId, out var name) ? name : "(adaptör bulunamadı)",
            profile.UseDhcp ? "DHCP" : "Statik",
            string.IsNullOrWhiteSpace(profile.IPv4Address) ? "—" : profile.IPv4Address,
            string.IsNullOrWhiteSpace(profile.Gateway) ? "—" : profile.Gateway,
            profile.DnsServers.Count == 0 ? "Otomatik / yok" : string.Join(", ", profile.DnsServers)
        )).ToArray();
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_adapters.Count == 0)
        {
            MessageBox.Show(this, "Önce en az bir ağ adaptörü algılanmalıdır.", "Profil eklenemedi",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new ProfileEditorWindow(_adapters) { Owner = this };
        if (editor.ShowDialog() == true && editor.Profile is { } profile)
        {
            _profiles.Add(profile);
            SaveProfiles("Profil kaydedildi.");
        }
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileRow row)
        {
            MessageBox.Show(this, "Düzenlemek için bir profil seçin.", "Profil seçilmedi",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new ProfileEditorWindow(_adapters, row.Profile) { Owner = this };
        if (editor.ShowDialog() == true && editor.Profile is { } updated)
        {
            var index = _profiles.FindIndex(p => p.Id == row.Profile.Id);
            if (index >= 0)
            {
                _profiles[index] = updated;
                SaveProfiles("Profil güncellendi.");
            }
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileRow row)
        {
            MessageBox.Show(this, "Silmek için bir profil seçin.", "Profil seçilmedi",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"'{row.Name}' profili silinsin mi?", "Profili sil",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _profiles.RemoveAll(p => p.Id == row.Profile.Id);
        SaveProfiles("Profil silindi.");
    }

    private void ImportProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Profil JSON dosyasını seç",
            Filter = "JSON dosyaları (*.json)|*.json|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var imported = _store.ImportFrom(dialog.FileName)
                .Select(p => p with { Id = Guid.NewGuid().ToString("N") })
                .ToList();
            _profiles.AddRange(imported);
            SaveProfiles($"{imported.Count} profil içe aktarıldı. Hiçbir ağ ayarı uygulanmadı.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Profil içe aktarılamadı: {ex.Message}", "İçe aktarma hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Profilleri dışa aktar",
            Filter = "JSON dosyaları (*.json)|*.json",
            FileName = "network-profiles.json",
            DefaultExt = ".json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _store.ExportTo(dialog.FileName, _profiles);
            StatusText.Text = $"{_profiles.Count} profil dışa aktarıldı.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Profiller dışa aktarılamadı: {ex.Message}", "Dışa aktarma hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveProfiles(string successMessage)
    {
        try
        {
            _store.Save(_profiles);
            RefreshProfiles();
            StatusText.Text = successMessage;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profil kaydedilemedi: {ex.Message}";
            MessageBox.Show(this, StatusText.Text, "Kayıt hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed record ProfileRow(
        NetworkProfile Profile,
        string Name,
        string AdapterName,
        string Mode,
        string IPv4Address,
        string Gateway,
        string Dns);
}