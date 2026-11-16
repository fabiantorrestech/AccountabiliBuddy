using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Remote;
using Material.Icons;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The noise player's controls: the sidebar's mini player and the on-screen window's play/pause and volume (shown
/// while Ctrl+Alt is held). Both show this one instance.
/// </summary>
public sealed partial class NoisePlayerViewModel : ObservableObject
{
    private readonly NoisePlayerService _noise;
    private NoiseCommandSender? _others;

    // True while showing the service's state, so the change isn't sent back to it.
    private bool _showing;

    public NoisePlayerViewModel(NoisePlayerService noise)
    {
        _noise = noise;
        TrackText = string.Empty;
        _noise.Changed += (_, _) => Show();
        _noise.HeadphonesNeeded += (_, _) => HeadphonesNeeded?.Invoke(this, EventArgs.Empty);
        Show();
    }

    /// <summary>
    /// ADR 92: the other devices' noise players for the volume popover's rows, and this PC's play and pause for them. Set by the app
    /// once sync is up; until then there are no rows.
    /// </summary>
    public void UseOtherDevices(NoiseCommandSender others)
    {
        _others = others;
        others.Changed += (_, _) => ShowOthers();
        ShowOthers();
    }

    /// <summary>The other devices with a noise player (ADR 92), as rows.</summary>
    public ObservableCollection<NoiseDeviceRowViewModel> OtherDevices { get; } = [];

    /// <summary>Any other device has a noise player: the Devices button shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OtherDevicesShown))]
    public partial bool HasOtherDevices { get; set; }

    /// <summary>The rows are open (remembered on this PC); they start hidden behind the Devices button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OtherDevicesShown), nameof(OtherDevicesTip))]
    public partial bool ShowsOtherDevices { get; set; }

    public bool OtherDevicesShown => HasOtherDevices && ShowsOtherDevices;

    public string OtherDevicesTip => ShowsOtherDevices ? "Hide other devices" : "Other devices' noise";

    [RelayCommand]
    private void ToggleOtherDevices() => _noise.SetShowsOtherDevices(!ShowsOtherDevices);

    [RelayCommand]
    private void ToggleOtherDevice(NoiseDeviceRowViewModel? row)
    {
        if (row is not null)
        {
            _others?.SetPlaying(row.Id, !row.Playing);
        }
    }

    private void ShowOthers()
    {
        IReadOnlyList<NoiseDeviceRow> rows = _others?.Others() ?? [];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OtherDevices.Clear();
        foreach (NoiseDeviceRow row in rows)
        {
            OtherDevices.Add(new NoiseDeviceRowViewModel(row, now));
        }

        HasOtherDevices = OtherDevices.Count > 0;
    }

    /// <summary>What the title bar's noise control scrolls through its name box when the lock stops or refuses play.</summary>
    public const string HeadphonesNoticeText = "Locked to headphones · connect headphones";

    /// <summary>
    /// Locked to headphones and none connected: raised each time the lock stops or refuses play (the title bar's noise control shows
    /// <see cref="HeadphonesNoticeText"/> once, then the sound's name again).
    /// </summary>
    public event EventHandler? HeadphonesNeeded;

    /// <summary>The sound's name (or why there's nothing to play), never a notice: the title bar's name box, which keeps its width.</summary>
    [ObservableProperty]
    public partial string NameText { get; set; } = string.Empty;

    /// <summary>The file playing or next, why there's nothing to play, or a short notice such as "Connect headphones".</summary>
    [ObservableProperty]
    public partial string TrackText { get; set; }

    /// <summary>The name being typed after the pencil was clicked.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    [ObservableProperty]
    public partial string RenameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasFiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayGlyph), nameof(PlayTip), nameof(PlayIcon))]
    public partial bool IsPlaying { get; set; }

    /// <summary>Segoe Fluent Icons: Pause or Play.</summary>
    public string PlayGlyph => IsPlaying ? "" : "";

    public string PlayTip => IsPlaying ? "Pause the noise" : "Play the noise";

    /// <summary>Material icon for what a press does now: Pause while playing, Play while paused (the overlay tile and panel).</summary>
    public MaterialIconKind PlayIcon => IsPlaying ? MaterialIconKind.Pause : MaterialIconKind.Play;

    /// <summary>The lock: plays only while headphones are connected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockIcon), nameof(LockTip))]
    public partial bool IsLocked { get; set; }

    public MaterialIconKind LockIcon => IsLocked ? MaterialIconKind.Lock : MaterialIconKind.LockOpenVariantOutline;

    public string LockTip => IsLocked
        ? "Locked to headphones: plays only while headphones are connected. Click to unlock."
        : "Lock to headphones: don't play unless headphones are connected";

    /// <summary>A notice such as "Connect headphones" is showing in place of the name.</summary>
    [ObservableProperty]
    public partial bool HasNotice { get; set; }

    /// <summary>0–100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    public partial double Volume { get; set; }

    public string VolumeText => $"{Volume:0}%";

    [ObservableProperty]
    public partial bool Repeat { get; set; }

    partial void OnVolumeChanged(double value)
    {
        if (!_showing)
        {
            _noise.SetVolume((int)Math.Round(value));
        }
    }

    partial void OnRepeatChanged(bool value)
    {
        if (!_showing)
        {
            _noise.SetRepeat(value);
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        _noise.TogglePlay();
    }

    [RelayCommand]
    private Task ToggleLockAsync()
    {
        return _noise.SetLockedToHeadphonesAsync(!_noise.IsLockedToHeadphones);
    }

    [RelayCommand]
    private void Next()
    {
        _noise.Next();
    }

    [RelayCommand]
    private void Previous()
    {
        _noise.Previous();
    }

    [RelayCommand]
    private void StartRename()
    {
        if (!HasFiles)
        {
            return;
        }

        RenameText = _noise.CurrentName ?? TrackText;
        IsRenaming = true;
    }

    [RelayCommand]
    private async Task SaveRenameAsync()
    {
        IsRenaming = false;
        await _noise.RenameCurrentAsync(RenameText);
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
    }

    private void Show()
    {
        _showing = true;
        NoiseSettings settings = _noise.Settings;
        HasFiles = _noise.HasFiles;
        HasNotice = _noise.Notice is not null;
        NameText = _noise.CurrentName ?? "No sound files in the folder (choose one in Settings)";
        TrackText = _noise.Notice ?? NameText;
        IsPlaying = _noise.IsPlaying;
        IsLocked = _noise.IsLockedToHeadphones;
        Volume = settings.Volume;
        Repeat = settings.Repeat;
        ShowsOtherDevices = settings.ShowsOtherDevices;
        _showing = false;
    }
}
