using System.Windows;
using NetworkProfiles.Models;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using NetworkProfiles.Services;

namespace NetworkProfiles;

public partial class ProfileEditorWindow : Window
{
    private readonly NetworkProfile? _original;
    public NetworkProfile? Profile { get; private set; }

    public ProfileEditorWindow(IReadOnlyList<AdapterStatus> adapters, NetworkProfile? profile = null)
    {
        InitializeComponent();
        _original = profile;
        AdapterBox.ItemsSource = adapters;

        if (profile is not null)
        {
            Title = "Edit profile";
            NameBox.Text = profile.Name;
            AdapterBox.SelectedValue = profile.AdapterId;
            DhcpCheckBox.IsChecked = profile.UseDhcp;
            AddressBox.Text = profile.IPv4Address ?? "";
            PrefixBox.Text = profile.PrefixLength?.ToString() ?? "24";
            GatewayBox.Text = profile.Gateway ?? "";
            DnsBox.Text = string.Join(", ", profile.DnsServers);
            NotesBox.Text = profile.Notes;
        }
        else
        {
            Title = "New profile";
            DhcpCheckBox.IsChecked = true;
            AdapterBox.SelectedIndex = 0;
            PrefixBox.Text = "24";
        }

        UpdateStaticFieldsState();
    }

    private void Dhcp_Changed(object sender, RoutedEventArgs e) => UpdateStaticFieldsState();

    private void UpdateStaticFieldsState()
    {
        var isStatic = DhcpCheckBox.IsChecked != true;
        AddressBox.IsEnabled = isStatic;
        PrefixBox.IsEnabled = isStatic;
        GatewayBox.IsEnabled = isStatic;
        DnsBox.IsEnabled = isStatic;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var useDhcp = DhcpCheckBox.IsChecked == true;
        int? prefix = int.TryParse(PrefixBox.Text.Trim(), out var parsedPrefix) ? parsedPrefix : null;
        var dnsServers = useDhcp
            ? new List<string>()
            : DnsBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

        var candidate = new NetworkProfile
        {
            Id = _original?.Id ?? Guid.NewGuid().ToString("N"),
            Name = NameBox.Text.Trim(),
            Notes = NotesBox.Text.Trim(),
            AdapterId = AdapterBox.SelectedValue as string ?? "",
            UseDhcp = useDhcp,
            IPv4Address = useDhcp ? null : AddressBox.Text.Trim(),
            PrefixLength = useDhcp ? null : prefix,
            Gateway = useDhcp || string.IsNullOrWhiteSpace(GatewayBox.Text) ? null : GatewayBox.Text.Trim(),
            DnsServers = dnsServers
        };

        var errors = ProfileValidationService.Validate(candidate);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Profile validation error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Profile = candidate;
        DialogResult = true;
    }
}