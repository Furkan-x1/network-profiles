using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using Microsoft.Win32;
using NetworkProfiles.Models;
using NetworkProfiles.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Application = System.Windows.Application;

namespace NetworkProfiles;

public partial class MainWindow : Window
{
    private readonly AdapterDiscoveryService _discovery = new();
    private readonly ProfileStore _store = new();
    private readonly StartupService _startup = new();
    private readonly NotifyIcon _trayIcon;
    private List<AdapterStatus> _adapters = [];
    private List<NetworkProfile> _profiles = [];
    private List<ProfileRow> _profileRows = [];
    private ICollectionView? _profileView;
    private bool _allowExit;

    public MainWindow()
    {
        InitializeComponent();
        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Network Profiles",
            Visible = true
        };
        _trayIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowWindow();
        };

        try { _profiles = _store.Load().ToList(); }
        catch (Exception ex) { StatusText.Text = $"Kayıtlı profiller okunamadı: {ex.Message}"; }

        try { SkipApplyConfirmationCheckBox.IsChecked = IsApplyConfirmationSkipped(); }
        catch { SkipApplyConfirmationCheckBox.IsChecked = false; }

        RefreshAdapters();
        RefreshProfiles();
        RebuildTrayMenu();
        try { StartupCheckBox.IsChecked = _startup.IsEnabled(); }
        catch (Exception ex) { StatusText.Text = $"Başlangıç ayarı okunamadı: {ex.Message}"; }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdapters();
        RefreshProfiles();
        RebuildTrayMenu();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDarkWindowFrame();

        if (Environment.GetCommandLineArgs().Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            Hide();
    }

    private void ApplyDarkWindowFrame()
    {
        // The WPF client area is dark already; set the native Windows 11
        // non-client frame explicitly so its four outer edges aren't white.
        var hwnd = new WindowInteropHelper(this).Handle;
        const int DwmwaBorderColor = 34;
        const int DwmwaCaptionColor = 35;
        const int DwmwaTextColor = 36;
        int darkColor = unchecked((int)0x00181311); // COLORREF for #111318
        int lightTextColor = unchecked((int)0x00F7F3F1); // COLORREF for #F1F3F7

        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref darkColor, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref darkColor, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref lightTextColor, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            _startup.SetEnabled(StartupCheckBox.IsChecked == true);
            StatusText.Text = StartupCheckBox.IsChecked == true
                ? "Windows başlangıcı etkin; uygulama sistem tepsisine küçültülerek açılacak."
                : "Windows başlangıcı devre dışı bırakıldı.";
        }
        catch (Exception ex)
        {
            StartupCheckBox.IsChecked = false;
            StatusText.Text = $"Başlangıç ayarı değiştirilemedi: {ex.Message}";
            MessageBox.Show(this, StatusText.Text, "Başlangıç ayarı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenScanner_Click(object sender, RoutedEventArgs e)
    {
        var scanner = new ScannerWindow { Owner = this };
        scanner.ShowDialog();
    }

    private void RefreshAdapters()
    {
        try
        {
            _adapters = _discovery.GetAdapters().ToList();
            AdaptersGrid.ItemsSource = _adapters;
            StatusText.Text = $"{_adapters.Count} adaptör bulundu. IPv4 adresi bulunan adaptörler üstte listelenir.";
        }
        catch (Exception ex) { StatusText.Text = $"Adaptör bilgileri okunamadı: {ex.Message}"; }
    }

    private void RefreshProfiles()
    {
        var adapterNames = _adapters.ToDictionary(a => a.Id, a => a.Name, StringComparer.OrdinalIgnoreCase);
        var matchingIds = new NetworkConfigurationService().GetMatchingProfileIds(_profiles);
        _profileRows = _profiles.Select(profile =>
        {
            var adapterName = adapterNames.TryGetValue(profile.AdapterId, out var name) ? name : "(adaptör bulunamadı)";
            return new ProfileRow(
                profile, profile.Name, adapterName,
                matchingIds.Contains(profile.Id) ? "Aktif" : "—",
                profile.UseDhcp ? "DHCP" : "Statik",
                string.IsNullOrWhiteSpace(profile.IPv4Address) ? "—" : profile.IPv4Address,
                string.IsNullOrWhiteSpace(profile.Gateway) ? "—" : profile.Gateway,
                profile.DnsServers.Count == 0 ? "Otomatik / yok" : string.Join(", ", profile.DnsServers));
        }).ToList();

        var selectedAdapter = AdapterFilterBox.SelectedItem as string;
        AdapterFilterBox.ItemsSource = new[] { "Tüm adaptörler" }
            .Concat(_profileRows.Select(r => r.AdapterName).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)).ToArray();
        AdapterFilterBox.SelectedItem = selectedAdapter is not null &&
            AdapterFilterBox.Items.Contains(selectedAdapter) ? selectedAdapter : "Tüm adaptörler";

        _profileView = CollectionViewSource.GetDefaultView(_profileRows);
        _profileView.Filter = FilterProfile;
        UpdateGrouping();
        ProfilesGrid.ItemsSource = _profileView;
        ProfileCountText.Text = $"Profil: {_profiles.Count} · Eşleşen aktif profil: {matchingIds.Count}";
        _profileView.Refresh();
    }

    private bool FilterProfile(object item)
    {
        if (item is not ProfileRow row) return false;
        var selectedAdapter = AdapterFilterBox.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(selectedAdapter) && selectedAdapter != "Tüm adaptörler"
            && !string.Equals(row.AdapterName, selectedAdapter, StringComparison.OrdinalIgnoreCase))
            return false;

        var query = ProfileSearchBox.Text.Trim();
        if (query.Length == 0) return true;
        return new[] { row.Name, row.AdapterName, row.Mode, row.IPv4Address, row.Gateway, row.Dns, row.Profile.Notes }
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshProfileFilter()
    {
        _profileView?.Refresh();
        if (ProfileCountText is not null)
            ProfileCountText.Text = $"Gösterilen: {_profileView?.Cast<object>().Count() ?? 0} / {_profiles.Count}";
    }

    private void ProfileSearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        RefreshProfileFilter();

    private void AdapterFilter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        RefreshProfileFilter();

    private void Grouping_Changed(object sender, RoutedEventArgs e) => UpdateGrouping();

    private void UpdateGrouping()
    {
        if (_profileView is null) return;
        _profileView.GroupDescriptions?.Clear();
        if (GroupByAdapterCheckBox.IsChecked == true)
            _profileView.GroupDescriptions?.Add(new PropertyGroupDescription(nameof(ProfileRow.AdapterName)));
        _profileView.Refresh();
    }

    private void ApplyConfirmation_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\NetworkProfiles");
            key?.SetValue("SkipApplyConfirmation", SkipApplyConfirmationCheckBox.IsChecked == true ? 1 : 0,
                RegistryValueKind.DWord);
            StatusText.Text = SkipApplyConfirmationCheckBox.IsChecked == true
                ? "Profil uygulama onayı kapatıldı. Sistem tepsisinden yapılan uygulamalar da doğrudan çalışacak."
                : "Profil uygulama onayı etkinleştirildi.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Tercih kaydedilemedi: {ex.Message}", "Ayar kaydedilemedi",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool IsApplyConfirmationSkipped()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\NetworkProfiles");
        return Convert.ToInt32(key?.GetValue("SkipApplyConfirmation", 0) ?? 0) == 1;
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

    private async void ApplySelected_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileRow row)
        {
            MessageBox.Show(this, "Uygulamak için bir profil seçin.", "Profil seçilmedi",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await ApplyProfileAsync(row.Profile);
    }

    private async Task ApplyProfileAsync(NetworkProfile profile)
    {
        var adapterName = _adapters.FirstOrDefault(a => a.Id == profile.AdapterId)?.Name ?? profile.AdapterId;
        var configuration = profile.UseDhcp
            ? "IPv4 ve DNS otomatik (DHCP)"
            : $"IPv4: {profile.IPv4Address}/{profile.PrefixLength}\nAğ geçidi: {profile.Gateway ?? "yok"}\nDNS: {(profile.DnsServers.Count == 0 ? "otomatik" : string.Join(", ", profile.DnsServers))}";

        if (SkipApplyConfirmationCheckBox.IsChecked != true &&
            MessageBox.Show(this,
                $"'{profile.Name}' profili '{adapterName}' adaptörüne uygulanacak.\n\n{configuration}\n\nMevcut ayarlar yalnızca işlem sırasında bellekte tutulur; hata oluşursa geri yükleme denenir. Dosyaya yedek oluşturulmaz. Devam edilsin mi?",
                "Ağ profilini uygula", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            StatusText.Text = "Windows ağ ayarları değiştiriliyor...";
            var result = await Task.Run(() => new NetworkConfigurationService().Apply(profile));

            StatusText.Text = result.Message;
            if (result.Success)
            {
                RefreshAdapters();
                RefreshProfiles();
                RebuildTrayMenu();
            }

            MessageBox.Show(this, result.Message,
                result.Success ? "Profil uygulandı" : "Profil uygulanamadı",
                MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profil uygulanamadı: {ex.Message}";
            MessageBox.Show(this, StatusText.Text, "Uygulama hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = _store.ImportFrom(dialog.FileName)
                .Select(p => p with { Id = Guid.NewGuid().ToString("N") }).ToList();
            if (_profiles.Count + imported.Count > 500)
                throw new InvalidDataException("En fazla 500 profil saklanabilir.");
            _profiles.AddRange(imported);
            SaveProfiles($"{imported.Count} profil içe aktarıldı. Ağ ayarları otomatik uygulanmadı.");
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
        if (dialog.ShowDialog(this) != true) return;
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
            RebuildTrayMenu();
            StatusText.Text = successMessage;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profil kaydedilemedi: {ex.Message}";
            MessageBox.Show(this, StatusText.Text, "Kayıt hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RebuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Network Profiles'ı aç", null, (_, _) => ShowWindow());
        var profilesMenu = new ToolStripMenuItem("Profili hızlı uygula");
        if (_profiles.Count == 0)
        {
            profilesMenu.DropDownItems.Add("(Kayıtlı profil yok)", null, null).Enabled = false;
        }
        else
        {
            foreach (var profile in _profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var adapterName = _adapters.FirstOrDefault(a => a.Id == profile.AdapterId)?.Name ?? "Adaptör bulunamadı";
                var item = new ToolStripMenuItem($"{profile.Name}  —  {adapterName}");
                item.ToolTipText = profile.UseDhcp
                    ? "DHCP / otomatik IP ve DNS"
                    : $"{profile.IPv4Address}/{profile.PrefixLength}";
                item.Click += async (_, _) => await ApplyProfileAsync(profile);
                profilesMenu.DropDownItems.Add(item);
            }
        }
        menu.Items.Add(profilesMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Yenile", null, (_, _) =>
        {
            RefreshAdapters();
            RefreshProfiles();
            RebuildTrayMenu();
        });
        menu.Items.Add("Çıkış", null, (_, _) => ExitApplication());
        var oldMenu = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    private void ShowWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit) return;
        e.Cancel = true;
        Hide();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosed(e);
    }

    private sealed record ProfileRow(
        NetworkProfile Profile, string Name, string AdapterName, string ActiveStatus, string Mode,
        string IPv4Address, string Gateway, string Dns);
}