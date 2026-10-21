using System.Windows;
using NetworkProfiles.Models;
using NetworkProfiles.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace NetworkProfiles;

public partial class QuickIpWindow : Window
{
    private readonly IReadOnlyList<AdapterStatus> _adapters;
    public NetworkProfile? Profile { get; private set; }

    public QuickIpWindow(IReadOnlyList<AdapterStatus> adapters)
    {
        InitializeComponent();
        _adapters = adapters;
        AdapterBox.ItemsSource = adapters;
        AdapterBox.SelectedIndex = adapters.Count > 0 ? 0 : -1;
        if (adapters.Count == 0)
        {
            AddressBox.IsEnabled = false;
            PrefixBox.IsEnabled = false;
            GatewayBox.IsEnabled = false;
            DnsBox.IsEnabled = false;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var adapterId = AdapterBox.SelectedValue as string ?? "";
        int? prefix = int.TryParse(PrefixBox.Text.Trim(), out var parsedPrefix) ? parsedPrefix : null;
        var candidate = new NetworkProfile
        {
            Name = "Quick IP (temporary)",
            Notes = "",
            AdapterId = adapterId,
            UseDhcp = false,
            IPv4Address = AddressBox.Text.Trim(),
            PrefixLength = prefix,
            Gateway = string.IsNullOrWhiteSpace(GatewayBox.Text) ? null : GatewayBox.Text.Trim(),
            DnsServers = DnsBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
        };

        var errors = ProfileValidationService.Validate(candidate);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Quick IP validation error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Profile = candidate;
        DialogResult = true;
    }
}
