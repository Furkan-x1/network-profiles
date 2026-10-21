using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
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
    private bool _traySuccessFeedbackRunning;
    private List<AdapterStatus> _adapters = [];
    private List<NetworkProfile> _profiles = [];
    private List<ProfileRow> _profileRows = [];
    private ICollectionView? _profileView;
    private bool _allowExit;

    private readonly bool _startMinimized;

    public MainWindow(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
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
        catch (Exception ex) { StatusText.Text = $"Could not load saved profiles: {ex.Message}"; }

        try { SkipApplyConfirmationCheckBox.IsChecked = IsApplyConfirmationSkipped(); }
        catch { SkipApplyConfirmationCheckBox.IsChecked = false; }

        RefreshAdapters();
        RefreshProfiles();
        RebuildTrayMenu();
        try { StartupCheckBox.IsChecked = _startup.IsEnabled(); }
        catch (Exception ex) { StatusText.Text = $"Could not read startup setting: {ex.Message}"; }
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

        if (_startMinimized)
            Hide();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (WindowState == WindowState.Normal)
            DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestoreWindow_Click(object sender, RoutedEventArgs e) =>
        ToggleMaximizeRestore();

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximizeRestore() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeRestoreButton is null) return;
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeRestoreButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private void ApplyDarkWindowFrame()
    {
        // The WPF client area is dark already; set the native Windows 11
        // non-client frame explicitly so its four outer edges aren't white.
        var hwnd = new WindowInteropHelper(this).Handle;
        const int DwmwaUseImmersiveDarkMode = 20;
        const int DwmwaBorderColor = 34;
        const int DwmwaCaptionColor = 35;
        const int DwmwaTextColor = 36;
        int darkMode = 1;
        int darkColor = unchecked((int)0x00181311); // COLORREF for #111318
        int lightTextColor = unchecked((int)0x00F7F3F1); // COLORREF for #F1F3F7

        // Enable the native dark title bar as well as explicitly coloring its frame.
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
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
                ? "Windows startup is enabled; the app will start minimized to the system tray."
                : "Windows startup is disabled.";
        }
        catch (Exception ex)
        {
            StartupCheckBox.IsChecked = false;
            StatusText.Text = $"Could not change startup setting: {ex.Message}";
            MessageBox.Show(this, StatusText.Text, "Startup settings", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            StatusText.Text = $"{_adapters.Count} adapters found.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not read adapter information: {ex.Message}"; }
    }

    private void RefreshProfiles()
    {
        var adapterNames = _adapters.ToDictionary(a => a.Id, a => a.Name, StringComparer.OrdinalIgnoreCase);
        var matchingIds = new NetworkConfigurationService().GetMatchingProfileIds(_profiles);
        _profileRows = _profiles.Select(profile =>
        {
            var adapterName = adapterNames.TryGetValue(profile.AdapterId, out var name) ? name : "(adapter not found)";
            return new ProfileRow(
                profile, profile.Name, adapterName,
                matchingIds.Contains(profile.Id) ? "Active" : "—",
                profile.UseDhcp ? "DHCP" : "Static",
                string.IsNullOrWhiteSpace(profile.IPv4Address) ? "—" : profile.IPv4Address,
                string.IsNullOrWhiteSpace(profile.Gateway) ? "—" : profile.Gateway,
                profile.DnsServers.Count == 0 ? "Automatic / none" : string.Join(", ", profile.DnsServers));
        }).ToList();

        var selectedAdapter = AdapterFilterBox.SelectedItem as string;
        AdapterFilterBox.ItemsSource = new[] { "All adapters" }
            .Concat(_profileRows.Select(r => r.AdapterName).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)).ToArray();
        AdapterFilterBox.SelectedItem = selectedAdapter is not null &&
            AdapterFilterBox.Items.Contains(selectedAdapter) ? selectedAdapter : "All adapters";

        _profileView = CollectionViewSource.GetDefaultView(_profileRows);
        _profileView.Filter = FilterProfile;
        UpdateGrouping();
        ProfilesGrid.ItemsSource = _profileView;
        ProfileCountText.Text = $"Profiles: {_profiles.Count} · Active matches: {matchingIds.Count}";
        _profileView.Refresh();
    }

    private bool FilterProfile(object item)
    {
        if (item is not ProfileRow row) return false;
        var selectedAdapter = AdapterFilterBox.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(selectedAdapter) && selectedAdapter != "All adapters"
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
            ProfileCountText.Text = $"Showing: {_profileView?.Cast<object>().Count() ?? 0} / {_profiles.Count}";
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
                ? "Apply confirmation disabled. Profiles applied from the system tray will also run immediately."
                : "Apply confirmation enabled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save preference: {ex.Message}", "Could not save setting",
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
            MessageBox.Show(this, "At least one network adapter must be detected first.", "Could not add profile",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var editor = new ProfileEditorWindow(_adapters) { Owner = this };
        if (editor.ShowDialog() == true && editor.Profile is { } profile)
        {
            _profiles.Add(profile);
            SaveProfiles("Profile saved.");
        }
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileRow row)
        {
            MessageBox.Show(this, "Select a profile to edit.", "No profile selected",
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
                SaveProfiles("Profile updated.");
            }
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileRow row)
        {
            MessageBox.Show(this, "Select a profile to delete.", "No profile selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Do you want to delete profile '{row.Name}'?", "Delete profile",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _profiles.RemoveAll(p => p.Id == row.Profile.Id);
        SaveProfiles("Profile deleted.");
    }

    private async void ApplySelected_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileRow row)
        {
            MessageBox.Show(this, "Select a profile to apply.", "No profile selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await ApplyProfileAsync(row.Profile);
    }

    private async Task ApplyProfileAsync(NetworkProfile profile, bool fromTray = false, bool skipConfirmation = false)
    {
        var adapterName = _adapters.FirstOrDefault(a => a.Id == profile.AdapterId)?.Name ?? profile.AdapterId;
        var configuration = profile.UseDhcp
            ? "IPv4 and DNS automatic (DHCP)"
            : $"IPv4: {profile.IPv4Address}/{profile.PrefixLength}\nGateway: {profile.Gateway ?? "none"}\nDNS: {(profile.DnsServers.Count == 0 ? "automatic" : string.Join(", ", profile.DnsServers))}";

        if (!skipConfirmation && SkipApplyConfirmationCheckBox.IsChecked != true &&
            MessageBox.Show(this,
                $"Profile '{profile.Name}' will be applied to adapter '{adapterName}'.\n\n{configuration}\n\nCurrent settings are kept in memory during the operation; rollback will be attempted if an error occurs. No backup file is created. Continue?",
                "Apply network profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            StatusText.Text = "Applying Windows network settings...";
            var result = await Task.Run(() => new NetworkConfigurationService().Apply(profile));

            StatusText.Text = result.Message;
            if (result.Success)
            {
                RefreshAdapters();
                RefreshProfiles();
                RebuildTrayMenu();
            }

            if (result.Success)
            {
                if (fromTray)
                    _ = ShowTraySuccessFeedbackAsync();
                else
                    MessageBox.Show(this, result.Message, "Profile applied",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, result.Message, "Could not apply profile",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not apply profile: {ex.Message}";
            MessageBox.Show(this, StatusText.Text, "Apply error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select profile JSON file",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = _store.ImportFrom(dialog.FileName)
                .Select(p => p with { Id = Guid.NewGuid().ToString("N") }).ToList();
            if (_profiles.Count + imported.Count > 500)
                throw new InvalidDataException("A maximum of 500 profiles can be stored.");
            _profiles.AddRange(imported);
            SaveProfiles($"Imported {imported.Count} profiles. Network settings were not applied automatically.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not import profiles: {ex.Message}", "Import error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export profiles",
            Filter = "JSON files (*.json)|*.json",
            FileName = "network-profiles.json",
            DefaultExt = ".json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _store.ExportTo(dialog.FileName, _profiles);
            StatusText.Text = $"Exported {_profiles.Count} profiles.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not export profiles: {ex.Message}", "Export error",
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
            StatusText.Text = $"Could not save profile: {ex.Message}";
            MessageBox.Show(this, StatusText.Text, "Save error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RebuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Network Profiles", null, (_, _) => ShowWindow());
        var profilesMenu = new ToolStripMenuItem("Quick apply profile");
        if (_profiles.Count == 0)
        {
            profilesMenu.DropDownItems.Add("(No saved profiles)", null, null).Enabled = false;
        }
        else
        {
            foreach (var profile in _profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var adapterName = _adapters.FirstOrDefault(a => a.Id == profile.AdapterId)?.Name ?? "Adapter not found";
                var item = new ToolStripMenuItem($"{profile.Name}  —  {adapterName}");
                item.ToolTipText = profile.UseDhcp
                    ? "DHCP / automatic IP and DNS"
                    : $"{profile.IPv4Address}/{profile.PrefixLength}";
                item.Click += async (_, _) => await ApplyProfileAsync(profile, fromTray: true);
                profilesMenu.DropDownItems.Add(item);
            }
        }
        menu.Items.Add(profilesMenu);
        menu.Items.Add("Quick IP...", null, (_, _) => OpenQuickIp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh", null, (_, _) =>
        {
            RefreshAdapters();
            RefreshProfiles();
            RebuildTrayMenu();
        });
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        var oldMenu = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    private async void OpenQuickIp()
    {
        var dialog = new QuickIpWindow(_adapters)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Profile is not null)
            await ApplyProfileAsync(dialog.Profile, fromTray: true, skipConfirmation: true);
    }

    private async Task ShowTraySuccessFeedbackAsync()
    {
        if (_traySuccessFeedbackRunning || _allowExit)
            return;

        _traySuccessFeedbackRunning = true;
        try
        {
            _trayIcon.Icon = System.Drawing.SystemIcons.Information;
            await Task.Delay(1400);
            if (!_allowExit)
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
        }
        finally
        {
            _traySuccessFeedbackRunning = false;
        }
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/Furkan-x1/network-profiles",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open GitHub: {ex.Message}", "Open GitHub",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowWindow()
    {
        // Tray-only startup creates the window with Opacity=0. Restore it before
        // showing/activating it; otherwise the window can remain invisible or render
        // incorrectly when opened from the notification area.
        ShowInTaskbar = true;
        Opacity = 1;

        if (!IsVisible)
            Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        ApplyDarkWindowFrame();
        InvalidateVisual();
        UpdateLayout();
        Activate();
        Focus();
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