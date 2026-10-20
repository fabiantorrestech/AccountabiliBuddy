using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Link;
using AccountabiliBuddy.Core.Link;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Setup tab → Linked devices (ADR 14): turn linking on or off, pair a phone or tablet with a QR code its camera opens
/// (with the code shown, to compare on the phone), and see or unpair the paired ones. Turning it off and unpairing
/// are locked during focus, like every Revoke.
/// </summary>
public sealed partial class LinkedDevicesViewModel : ObservableObject, IDisposable
{
    private const int QrPixelsPerModule = 8;

    private readonly LinkService _link;
    private readonly SessionController _sessions;
    private readonly DispatcherTimer _countdown;
    private PairingOffer? _offer;

    public LinkedDevicesViewModel(LinkService link, SessionController sessions)
    {
        _link = link;
        _sessions = sessions;
        Status = PairingDetail = string.Empty;
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) => RefreshPairing();

        _link.Changed += (_, _) => Refresh();
        _link.DevicePaired += (_, device) =>
        {
            StopPairing();
            Message = $"{device.Name} is linked.";
        };
        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;
        Refresh();
    }

    public ObservableCollection<LinkedDeviceRowViewModel> Devices { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TurnOnCommand), nameof(TurnOffCommand), nameof(PairCommand))]
    public partial bool IsEnabled { get; set; }

    /// <summary>True during focus: turning off and unpairing are disabled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TurnOffCommand), nameof(UnpairCommand))]
    public partial bool IsLocked { get; set; }

    /// <summary>E.g. "On · 1 device connected" or why it isn't working.</summary>
    [ObservableProperty]
    public partial string Status { get; set; }

    /// <summary>The last action's result (e.g. "Pixel 7 is linked.").</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool IsPairing { get; set; }

    [ObservableProperty]
    public partial Bitmap? QrImage { get; set; }

    /// <summary>The code, grouped for reading (e.g. "ABCD-EF23"), to compare with the one the phone shows.</summary>
    [ObservableProperty]
    public partial string? PairingCode { get; set; }

    /// <summary>E.g. "Expires in 4:32 · 192.168.1.20, port 47821".</summary>
    [ObservableProperty]
    public partial string PairingDetail { get; set; }

    public void Dispose()
    {
        _countdown.Stop();
    }

    private bool CanTurnOn => !IsEnabled;

    [RelayCommand(CanExecute = nameof(CanTurnOn))]
    private async Task TurnOnAsync()
    {
        Message = await _link.SetEnabledAsync(true);
    }

    private bool CanTurnOff => IsEnabled && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanTurnOff))]
    private async Task TurnOffAsync()
    {
        StopPairing();
        Message = await _link.SetEnabledAsync(false);
    }

    private bool CanPair => IsEnabled;

    [RelayCommand(CanExecute = nameof(CanPair))]
    private void Pair()
    {
        PairingOffer? offer = _link.BeginPairing();
        if (offer is null)
        {
            Message = _link.Problem ?? "Linking isn't running.";
            return;
        }

        _offer = offer;
        QrImage?.Dispose();
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(offer.Link, QRCodeGenerator.ECCLevel.M);
        byte[] png = new PngByteQRCode(data).GetGraphic(QrPixelsPerModule);
        QrImage = new Bitmap(new MemoryStream(png));
        PairingCode = $"{offer.Code[..4]}-{offer.Code[4..]}";
        IsPairing = true;
        Message = null;
        RefreshPairing();
        _countdown.Start();
    }

    [RelayCommand]
    private void CancelPairing()
    {
        _link.CancelPairing();
        StopPairing();
    }

    private bool CanUnpair => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanUnpair))]
    private async Task UnpairAsync(LinkedDeviceRowViewModel row)
    {
        Message = await _link.UnpairAsync(row.Id) ?? $"{row.Name} is unpaired.";
    }

    private void RefreshPairing()
    {
        if (_offer is null)
        {
            return;
        }

        TimeSpan left = _offer.ExpiresAt - _sessions.Now;
        if (left <= TimeSpan.Zero)
        {
            StopPairing();
            Message = "The pairing code expired. Pair again for a new one.";
            return;
        }

        PairingDetail = $"Expires in {(int)left.TotalMinutes}:{left.Seconds:00} · {_offer.Address}, port {_offer.Port}";
    }

    private void StopPairing()
    {
        _countdown.Stop();
        _offer = null;
        IsPairing = false;
        QrImage?.Dispose();
        QrImage = null;
        PairingCode = null;
    }

    private void Refresh()
    {
        IsEnabled = _link.IsEnabled;
        IReadOnlyCollection<Guid> connected = _link.ConnectedDeviceIds;
        Devices.Clear();
        foreach (PairedDevice device in _link.Devices.OrderBy(device => device.PairedAt))
        {
            string when = connected.Contains(device.Id) ? "connected now"
                : device.LastSeenAt is DateTimeOffset seen ? $"last seen {seen.ToLocalTime():ddd d MMM, t}"
                : "not seen yet";
            Devices.Add(new LinkedDeviceRowViewModel(device.Id, device.Name, $"{device.Kind} · {when}"));
        }

        Status = !IsEnabled ? "Off. Turn it on to pair a phone or tablet."
            : _link.Problem is string problem ? problem
            : connected.Count > 0 ? $"On · {connected.Count} device{(connected.Count == 1 ? "" : "s")} connected"
            : $"On · listening on port {_link.Port}";
    }
}
