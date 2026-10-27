using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Devices → "Run a relay on this PC" (ADR 46): the switch (explained first, then Windows' one permission prompt for the
/// firewall rule), the relay's status, start / stop / restart, its addresses and pin, the port, "also on public networks",
/// the storage it uses with Clear, the notice on a public network and the last 50 lines of its log. It changes only the
/// relay and the sync settings, never an enforcement setting, so a focus session doesn't lock it. Use from the UI thread only.
/// </summary>
public sealed partial class RelayHostViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(5);
    private const int SlowRefreshEvery = 6;

    private readonly RelayHostService _host;
    private readonly SyncSettingsService _settings;
    private readonly INetworkTrust _networkTrust;
    private readonly DispatcherTimer _timer;
    private int _ticks;
    private int _lastShownPort;
    private bool _loading;
    private bool _checkingNetwork;

    public RelayHostViewModel(RelayHostService host, SyncSettingsService settings, INetworkTrust networkTrust)
    {
        _host = host;
        _settings = settings;
        _networkTrust = networkTrust;
        StatusText = string.Empty;
        PortText = string.Empty;
        PinText = string.Empty;
        StorageText = string.Empty;
        LogText = string.Empty;
        host.Changed += (_, _) => Refresh();
        settings.Changed += (_, _) => Refresh();
        _timer = new DispatcherTimer { Interval = RefreshEvery };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        Refresh();
        StorageText = FormatBytes(_host.Files.StorageBytes());
        _ = CheckFirewallAsync();
        _ = CheckPublicNetworkAsync();
    }

    public ObservableCollection<string> ShareAddresses { get; } = [];

    /// <summary>The switch is on: the relay runs whenever the app runs.</summary>
    [ObservableProperty]
    public partial bool HostOn { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>"Running on port 8743, 2 devices connected", "Stopped", or what is wrong.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>The port as typed; applied with <see cref="SetPortCommand"/>.</summary>
    [ObservableProperty]
    public partial string PortText { get; set; }

    [ObservableProperty]
    public partial bool AlsoPublic { get; set; }

    /// <summary>The address a phone should use, picked from <see cref="ShareAddresses"/>; the join QR carries it.</summary>
    [ObservableProperty]
    public partial string? SelectedShareAddress { get; set; }

    /// <summary>The relay certificate's fingerprint; empty until the relay has made its certificate.</summary>
    [ObservableProperty]
    public partial string PinText { get; set; }

    [ObservableProperty]
    public partial string StorageText { get; set; }

    [ObservableProperty]
    public partial string? LastChangeText { get; set; }

    [ObservableProperty]
    public partial string LogText { get; set; }

    /// <summary>Whether Windows' firewall has the rule for the relay program; null while not looked up yet.</summary>
    [ObservableProperty]
    public partial bool? HasFirewallRule { get; set; }

    /// <summary>Set while this PC is on a public network with the relay running and reachable there; null otherwise.</summary>
    [ObservableProperty]
    public partial string? PublicNetworkNotice { get; set; }

    /// <summary>The explanation before the switch goes on is showing.</summary>
    [ObservableProperty]
    public partial bool ConfirmingTurnOn { get; set; }

    /// <summary>The question before the stored files are deleted is showing.</summary>
    [ObservableProperty]
    public partial bool ConfirmingClear { get; set; }

    /// <summary>The last thing that happened, or null.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>True when this PC's Own relay method points at some other relay: turning the switch on replaces it.</summary>
    public bool ReplacesOtherRelay => _settings.Current.RelayOn && !string.IsNullOrWhiteSpace(_settings.Current.RelayUrl) && !IsOwnAddress(_settings.Current.RelayUrl);

    /// <summary>The address and pin for the join QR, or null when the relay isn't hosted here or its pin isn't known yet.</summary>
    public (string Address, string Pin)? JoinTarget =>
        HostOn && SelectedShareAddress is not null && PinText.Length > 0 ? (SelectedShareAddress, PinText) : null;

    /// <summary>The relay runs here but Windows has no firewall rule for it: other devices may be blocked.</summary>
    public bool ShowAddFirewallRule => HostOn && HasFirewallRule == false;

    /// <summary>The relay is off but its firewall rule is still there.</summary>
    public bool ShowRemoveFirewallRule => !HostOn && HasFirewallRule == true;

    public void Dispose()
    {
        _timer.Stop();
    }

    partial void OnHostOnChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAddFirewallRule));
        OnPropertyChanged(nameof(ShowRemoveFirewallRule));
    }

    partial void OnHasFirewallRuleChanged(bool? value)
    {
        OnPropertyChanged(nameof(ShowAddFirewallRule));
        OnPropertyChanged(nameof(ShowRemoveFirewallRule));
    }

    /// <summary>Builds every shown value again from the service and the settings.</summary>
    public void Refresh()
    {
        SyncSettings settings = _settings.Current;
        _loading = true;
        HostOn = settings.HostRelayOn;
        AlsoPublic = settings.HostRelayPublic;
        _loading = false;
        IsRunning = _host.IsRunning;
        if (settings.HostRelayPort != _lastShownPort)
        {
            // Only a changed setting replaces what is being typed.
            _lastShownPort = settings.HostRelayPort;
            PortText = settings.HostRelayPort.ToString();
        }

        PinText = _host.Files.ReadPin() ?? string.Empty;
        StatusText = BuildStatus(settings);
        LastChangeText = _host.LastChange is { } at ? $"Last change on the relay: {at.LocalDateTime:h:mm tt}" : null;
        LogText = string.Join(Environment.NewLine, _host.LogLines);
        RefreshAddresses(settings.HostRelayPort);
        OnPropertyChanged(nameof(ReplacesOtherRelay));
        OnPropertyChanged(nameof(JoinTarget));
    }

    [RelayCommand]
    private void BeginTurnOn()
    {
        if (!_host.ProgramExists)
        {
            Message = "The relay program isn't next to the app. Reinstall or update AccountabiliBuddy.";
            return;
        }

        ConfirmingTurnOn = true;
        Message = null;
    }

    [RelayCommand]
    private void CancelTurnOn()
    {
        ConfirmingTurnOn = false;
    }

    [RelayCommand]
    private async Task ConfirmTurnOnAsync()
    {
        ConfirmingTurnOn = false;
        Message = "Windows will ask for permission to open the firewall for the relay…";
        FirewallResult result = await _host.AllowFirewallAsync();
        await _host.TurnOnAsync();
        Message = result switch
        {
            FirewallResult.Done => "The relay is on. This PC now syncs through it too.",
            FirewallResult.Declined => "The relay is on, but you didn't give Windows' permission: it runs, yet Windows may block your phone from reaching it. This PC still syncs through it. You can add the firewall rule below.",
            _ => "The relay is on, but the firewall rule couldn't be made, so Windows may block your phone from reaching it. This PC still syncs through it.",
        };
        await CheckFirewallAsync();
    }

    [RelayCommand]
    private async Task TurnOffAsync()
    {
        await _host.TurnOffAsync();
        Message = "The relay is off. Your other devices can't sync through this PC until you turn it on again. The Windows firewall rule stays, so turning it on again needs no prompt; remove it below if you like.";
        PublicNetworkNotice = null;
        await CheckFirewallAsync();
    }

    [RelayCommand]
    private void StartRelay()
    {
        _host.StartNow();
    }

    /// <summary>Ends the relay until "Start" or the next launch of the app; the switch stays on.</summary>
    [RelayCommand]
    private void StopRelay()
    {
        _host.StopNow();
        PublicNetworkNotice = null;
        Message = "The relay is stopped for now. It starts again with the app, or press Start.";
    }

    [RelayCommand]
    private void RestartRelay()
    {
        _host.Restart();
    }

    [RelayCommand]
    private async Task SetPortAsync()
    {
        if (!int.TryParse(PortText.Trim(), out int port) || port is < 1024 or > 65535)
        {
            Message = "The port must be a number from 1024 to 65535.";
            return;
        }

        if (port == _settings.Current.HostRelayPort)
        {
            return;
        }

        await _host.SetPortAsync(port);
        Message = $"Port {port} saved. A phone that joined by QR needs a new QR code (Link a device).";
    }

    partial void OnAlsoPublicChanged(bool value)
    {
        if (_loading || value == _settings.Current.HostRelayPublic)
        {
            return;
        }

        _ = ChangePublicNetworksAsync(value);
    }

    private async Task ChangePublicNetworksAsync(bool alsoPublic)
    {
        await _host.SetPublicNetworksAsync(alsoPublic);
        if (!_host.IsOn || !await Task.Run(() => _host.HasFirewallRule))
        {
            Message = alsoPublic ? "Public networks will be included when the firewall rule is made." : "Public networks will be left out when the firewall rule is made.";
            return;
        }

        Message = "Windows will ask for permission to change the firewall rule…";
        FirewallResult result = await _host.AllowFirewallAsync();
        Message = result == FirewallResult.Done
            ? (alsoPublic ? "The relay is now reachable on public networks too." : "The relay is now reachable on private networks only.")
            : "The firewall rule wasn't changed (Windows' permission was refused), so it still covers what it did before.";
        await CheckFirewallAsync();
    }

    [RelayCommand]
    private async Task AddFirewallRuleAsync()
    {
        Message = "Windows will ask for permission to open the firewall for the relay…";
        FirewallResult result = await _host.AllowFirewallAsync();
        Message = result switch
        {
            FirewallResult.Done => "The firewall rule is in place.",
            FirewallResult.Declined => "Windows' permission was refused, so the rule wasn't made.",
            _ => "The firewall rule couldn't be made.",
        };
        await CheckFirewallAsync();
    }

    [RelayCommand]
    private async Task RemoveFirewallRuleAsync()
    {
        Message = "Windows will ask for permission to remove the firewall rule…";
        FirewallResult result = await _host.RemoveFirewallRuleAsync();
        Message = result switch
        {
            FirewallResult.Done => "The firewall rule is removed.",
            FirewallResult.Declined => "Windows' permission was refused, so the rule is still there.",
            _ => "The firewall rule couldn't be removed.",
        };
        await CheckFirewallAsync();
    }

    [RelayCommand]
    private void BeginClear()
    {
        ConfirmingClear = true;
    }

    [RelayCommand]
    private void CancelClear()
    {
        ConfirmingClear = false;
    }

    [RelayCommand]
    private void ConfirmClear()
    {
        ConfirmingClear = false;
        string? problem = _host.ClearStorage();
        Message = problem ?? "Deleted the stored sync files. Your devices write theirs again within minutes.";
        RefreshSlowParts();
    }

    private string BuildStatus(SyncSettings settings)
    {
        if (!settings.HostRelayOn)
        {
            return "Off";
        }

        if (_host.IsRunning)
        {
            int devices = RelayHostFiles.ConnectedDevices(settings.HostRelayPort);
            string connected = devices == 0 ? "no other device connected" : devices == 1 ? "1 device connected" : $"{devices} devices connected";
            return $"Running on port {settings.HostRelayPort}, {connected}";
        }

        return _host.Problem ?? "Stopped. It starts again with the app, or press Start.";
    }

    private void RefreshAddresses(int port)
    {
        IReadOnlyList<string> addresses = _host.IsRunning || HostOn ? RelayHostFiles.Addresses(port) : [];
        if (!addresses.SequenceEqual(ShareAddresses))
        {
            ShareAddresses.Clear();
            foreach (string address in addresses)
            {
                ShareAddresses.Add(address);
            }
        }

        if (SelectedShareAddress is null || !ShareAddresses.Contains(SelectedShareAddress))
        {
            SelectedShareAddress = ShareAddresses.FirstOrDefault();
        }
    }

    private void OnTick()
    {
        if (HostOn)
        {
            Refresh();
        }

        _ticks++;
        if (_ticks % SlowRefreshEvery == 0)
        {
            RefreshSlowParts();
        }
    }

    // The parts that touch the disk, the firewall or the network list: not every few seconds.
    private void RefreshSlowParts()
    {
        StorageText = FormatBytes(_host.Files.StorageBytes());
        if (HostOn)
        {
            _ = CheckFirewallAsync();
            _ = CheckPublicNetworkAsync();
        }
    }

    private async Task CheckFirewallAsync()
    {
        HasFirewallRule = await Task.Run(() => _host.HasFirewallRule);
    }

    private async Task CheckPublicNetworkAsync()
    {
        if (_checkingNetwork)
        {
            return;
        }

        _checkingNetwork = true;
        try
        {
            bool reachable = _host.IsRunning && _settings.Current.HostRelayPublic;
            PublicNetwork? network = reachable ? await Task.Run(_networkTrust.FindPublicNetwork) : null;
            PublicNetworkNotice = network is null
                ? null
                : $"Your relay is reachable on this public network ({network.Name}); your data stays sealed and HTTPS-pinned.";
        }
        finally
        {
            _checkingNetwork = false;
        }
    }

    private static bool IsOwnAddress(string? url)
    {
        return RelayAddress.TryNormalize(url, out Uri? address) && (address.IsLoopback || address.Host == "localhost");
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} bytes",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        };
    }
}
