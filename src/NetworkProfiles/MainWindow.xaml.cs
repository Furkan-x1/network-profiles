using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
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
    private readonly NotifyIcon _trayIcon;
    private List<AdapterStatus> _adapters = [];
    private List<NetworkProfile> _profiles = [];
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

        RefreshAdapters();
        RefreshProfiles();
        RebuildTrayMenu();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdapters();
        RefreshProfiles();
        RebuildTrayMenu();
    }

    private void RefreshAdapters()
    {
        try
        {
            _adapters = _discovery.GetAdapters().ToList();
            AdaptersGrid.ItemsSource = _adapters;
            StatusText.Text = $"{_adapters.Count} adaptör bulundu. Bağlı adaptörler üstte listelenir.";
        }
        catch (Exception ex) { StatusText.Text = $"Adaptör bilgileri okunamadı: {ex.Message}"; }
    }

    private void RefreshProfiles()
    {
        var adapterNames = _adapters.ToDictionary(a => a.Id, a => a.Name, StringComparer.OrdinalIgnoreCase);
        ProfilesGrid.ItemsSource = _profiles.Select(profile => new ProfileRow(
            profile, profile.Name,
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

        if (MessageBox.Show(this,
            $"'{profile.Name}' profili '{adapterName}' adaptörüne uygulanacak.\n\n{configuration}\n\nMevcut ayarlar yedeklenecek; doğrulama başarısız olursa geri yükleme denenecek. Devam edilsin mi?",
            "Ağ profilini uygula", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkProfiles", "requests");
        Directory.CreateDirectory(folder);
        var requestId = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(folder, requestId + ".json");
        var resultPath = Path.ChangeExtension(requestPath, ".result.json");

        try
        {
            File.WriteAllText(requestPath, JsonSerializer.Serialize(new ApplyRequest(profile, requestId)));
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                throw new InvalidOperationException("Uygulamanın EXE yolu bulunamadı.");

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--apply-request \"{requestPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            }) ?? throw new InvalidOperationException("Yönetici işlemi başlatılamadı.");

            StatusText.Text = "Windows ağ ayarları değiştiriliyor; UAC onayı bekleniyor...";
            await process.WaitForExitAsync();

            if (!File.Exists(resultPath))
                throw new InvalidOperationException("Yönetici işlemi sonuç dosyası üretmedi. UAC iptal edilmiş veya işlem beklenmedik biçimde sonlanmış olabilir.");

            var result = JsonSerializer.Deserialize<ApplyResult>(File.ReadAllText(resultPath))
                ?? throw new InvalidDataException("Uygulama sonucu okunamadı.");
            StatusText.Text = result.Message;
            _trayIcon.ShowBalloonTip(5000, "Network Profiles", result.Message,
                result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);

            if (!string.IsNullOrWhiteSpace(result.BackupPath))
                StatusText.Text += $" Yedek: {result.BackupPath}";

            if (result.Success)
                RefreshAdapters();

            MessageBox.Show(this, result.Message +
                (string.IsNullOrWhiteSpace(result.BackupPath) ? "" : $"\n\nYedek: {result.BackupPath}"),
                result.Success ? "Profil uygulandı" : "Profil uygulanamadı",
                MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusText.Text = "Yönetici izni verilmedi; ağ ayarları değiştirilmedi.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profil uygulanamadı: {ex.Message}";
            MessageBox.Show(this, StatusText.Text, "Uygulama hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { if (File.Exists(requestPath)) File.Delete(requestPath); } catch { }
            try { if (File.Exists(resultPath)) File.Delete(resultPath); } catch { }
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
        _trayIcon.ShowBalloonTip(1800, "Network Profiles",
            "Uygulama sistem tepsisinde çalışmaya devam ediyor.", ToolTipIcon.Info);
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
        NetworkProfile Profile, string Name, string AdapterName, string Mode,
        string IPv4Address, string Gateway, string Dns);
}