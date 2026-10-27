using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using QRCoder;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Devices tab (ADR 22): this PC and every device in its group with how fresh each is, today's focus across them,
/// the sharing methods safest first (with the folder, interval and QR reminder), and linking and leaving the group.
/// The switches and Leave are locked during a focus session the user started, like other Revokes. Use from the UI thread only.
/// </summary>
public sealed partial class DevicesViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(1);
    private static readonly int[] SnoozeChoices = [5, 15, 30, 60];

    private readonly SyncIdentityService _identity;
    private readonly SyncSettingsService _settings;
    private readonly ReceivedDevicesService _received;
    private readonly SyncCoordinator _coordinator;
    private readonly SessionController _sessions;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;

    // True while the switches are set from the saved settings (not by the user).
    private bool _loading;
    private DriveAccountService? _driveAccount;
    private SyncRelayService? _relay;

    // The relay settings the address and pin boxes were last filled from, so a refresh doesn't overwrite what is being typed.
    private string? _loadedRelayUrl;
    private string? _loadedRelayPin;

    public DevicesViewModel(
        SyncIdentityService identity,
        SyncSettingsService settings,
        ReceivedDevicesService received,
        SyncCoordinator coordinator,
        SessionController sessions,
        TimeProvider time)
    {
        _identity = identity;
        _settings = settings;
        _received = received;
        _coordinator = coordinator;
        _sessions = sessions;
        _time = time;
        CombinedTodayText = string.Empty;
        DriveStatusText = string.Empty;
        RelayUrlText = string.Empty;
        RelayPinText = string.Empty;
        RelayStatusText = string.Empty;
        JoinCode = string.Empty;
        IsLocked = sessions.IsEnforcing;
        sessions.StateChanged += (_, _) => IsLocked = sessions.IsEnforcing;
        identity.Changed += (_, _) => Refresh();
        settings.Changed += (_, _) => Refresh();
        received.Changed += (_, _) => Refresh();
        _timer = new DispatcherTimer { Interval = RefreshEvery };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    /// <summary>Opens today's sync QR codes; set by the app.</summary>
    public Action? ShowSyncQr { get; set; }

    /// <summary>Writes this PC's sync file now and hands it to connected devices; set by the app.</summary>
    public Func<Task>? SyncNow { get; set; }

    /// <summary>Takes in sync files and pictures of the phone's codes; set by the app.</summary>
    public SyncImportService? Import { get; set; }

    /// <summary>The Drive sign-in (ADR 26); set by the app.</summary>
    public DriveAccountService? DriveAccount
    {
        get => _driveAccount;
        set
        {
            _driveAccount = value;
            if (value is not null)
            {
                value.Changed += (_, _) => Refresh();
            }

            Refresh();
        }
    }

    /// <summary>The own-relay method (ADR 46): its status and the connection test; set by the app.</summary>
    public SyncRelayService? Relay
    {
        get => _relay;
        set
        {
            _relay = value;
            if (value is not null)
            {
                value.StatusChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
            }

            Refresh();
        }
    }

    /// <summary>Opens Google's sign-in page in the browser; set by the app.</summary>
    public Func<Uri, bool>? OpenBrowser { get; set; }

    public ObservableCollection<DeviceRowViewModel> Rows { get; } = new();

    /// <summary>E.g. "Today: 5h 10m focused across 3 devices".</summary>
    [ObservableProperty]
    public partial string CombinedTodayText { get; set; }

    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    public partial bool InGroup { get; set; }

    /// <summary>The group's link code in groups of 5, shown after "Link a device"; null until then.</summary>
    [ObservableProperty]
    public partial string? LinkCodeText { get; set; }

    [ObservableProperty]
    public partial Bitmap? LinkQrImage { get; set; }

    /// <summary>A link code typed in to join another device's group.</summary>
    [ObservableProperty]
    public partial string JoinCode { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool FolderOn { get; set; }

    [ObservableProperty]
    public partial string? FolderPath { get; set; }

    [ObservableProperty]
    public partial bool QrOn { get; set; }

    [ObservableProperty]
    public partial bool FileShareOn { get; set; }

    /// <summary>The Google Drive method (ADR 26).</summary>
    [ObservableProperty]
    public partial bool DriveOn { get; set; }

    /// <summary>"Signed in to Google Drive", or what's missing before Drive can be used.</summary>
    [ObservableProperty]
    public partial string DriveStatusText { get; set; }

    [ObservableProperty]
    public partial bool DriveSignedIn { get; set; }

    /// <summary>The own-relay method (ADR 46).</summary>
    [ObservableProperty]
    public partial bool RelayOn { get; set; }

    /// <summary>The relay's address as typed; saved with <see cref="SaveRelayCommand"/>.</summary>
    [ObservableProperty]
    public partial string RelayUrlText { get; set; }

    /// <summary>The relay certificate's pin as typed; empty for a relay with a normal certificate.</summary>
    [ObservableProperty]
    public partial string RelayPinText { get; set; }

    /// <summary>"Connected: changes arrive in about a second", or what is wrong; empty while the method is off.</summary>
    [ObservableProperty]
    public partial string RelayStatusText { get; set; }

    /// <summary>The result of "Test connection"; null until one ran.</summary>
    [ObservableProperty]
    public partial string? RelayTestText { get; set; }

    /// <summary>The sync interval as the list's index: Instant, 1 min, 5 min, 15 min.</summary>
    [ObservableProperty]
    public partial int IntervalIndex { get; set; }

    /// <summary>True: the QR reminder comes when the day plan's last Work part ends; false: at <see cref="ReminderAt"/>.</summary>
    [ObservableProperty]
    public partial bool ReminderAtWorkEnd { get; set; }

    [ObservableProperty]
    public partial TimeSpan? ReminderAt { get; set; }

    /// <summary>"Later" waits 5, 15, 30 or 60 minutes, as the list's index.</summary>
    [ObservableProperty]
    public partial int SnoozeIndex { get; set; }

    public bool HasLinkCode => LinkCodeText is not null;

    /// <summary>Builds the rows and switches again from what's saved and received.</summary>
    public void Refresh()
    {
        SyncSettings settings = _settings.Current;
        InGroup = _identity.Key is not null;
        _loading = true;
        FolderOn = settings.FolderOn;
        FolderPath = settings.FolderPath;
        QrOn = settings.QrOn;
        FileShareOn = settings.FileShareOn;
        DriveOn = settings.DriveOn;
        RelayOn = settings.RelayOn;
        if (settings.RelayUrl != _loadedRelayUrl || settings.RelayPin != _loadedRelayPin)
        {
            _loadedRelayUrl = settings.RelayUrl;
            _loadedRelayPin = settings.RelayPin;
            RelayUrlText = settings.RelayUrl ?? string.Empty;
            RelayPinText = settings.RelayPin ?? string.Empty;
        }

        RelayStatusText = !settings.RelayOn ? string.Empty
            : _relay?.Problem ?? (_relay?.IsConnected == true ? "Connected: changes arrive in about a second." : "Connecting…");
        DriveSignedIn = _driveAccount?.IsSignedIn == true;
        DriveStatusText = _driveAccount is null || !_driveAccount.HasClient && !DriveSignedIn
            ? "Set up your Google client first: Settings → Calendars → Google setup guide (turn on the Drive API there too)."
            : DriveSignedIn
                ? $"Signed in to Google Drive. The app sees only the files it made, in the folder \"{GoogleDriveFiles.FolderName}\"."
                : "Not signed in to Google Drive.";
        IntervalIndex = (int)settings.Interval;
        ReminderAtWorkEnd = settings.ReminderAt is null;
        ReminderAt = settings.ReminderAt?.ToTimeSpan() ?? new TimeSpan(17, 0, 0);
        SnoozeIndex = Math.Max(0, Array.IndexOf(SnoozeChoices, settings.SnoozeMinutes));
        _loading = false;

        DateTimeOffset now = _time.GetUtcNow();
        Rows.Clear();
        Rows.Add(new DeviceRowViewModel(
            _identity.DeviceName,
            "This PC",
            MethodsText(OwnMethods(settings)),
            InGroup ? string.Empty : "Not linked to your other devices yet",
            InGroup ? DeviceSyncState.ThisDevice : DeviceSyncState.Unlinked,
            InGroup ? "This PC" : "Unlinked"));
        foreach (ReceivedDevice device in _received.Devices.OrderByDescending(device => device.File.Device.WrittenAt))
        {
            SyncDeviceCard card = device.File.Device;
            TimeSpan age = now - card.WrittenAt;
            // A PC writes at this PC's interval; a phone or watch sends when it's opened, so a day is its interval.
            TimeSpan interval = card.Kind == SyncDeviceCard.KindPc ? IntervalOf(settings.Interval) : TimeSpan.FromDays(1);
            DeviceSyncState state = DeviceRowViewModel.StateFor(age < TimeSpan.Zero ? TimeSpan.Zero : age, interval);
            Rows.Add(new DeviceRowViewModel(
                card.Name,
                KindName(card.Kind),
                MethodsText(card.Methods),
                $"{Ago(age)} · {MethodName(device.Method)}",
                state,
                state == DeviceSyncState.Behind ? $"Behind by {Duration(age - interval)}" : "Up to date"));
            foreach (SyncCompanion companion in card.Companions ?? [])
            {
                Rows.Add(CompanionRow(companion, card.Name, now));
            }
        }

        _ = UpdateTodayAsync(now);
    }

    /// <summary>
    /// A phone's linked watch under it (Task 18), display only: paired and unpaired on the phone.
    /// </summary>
    private DeviceRowViewModel CompanionRow(SyncCompanion companion, string phoneName, DateTimeOffset now)
    {
        string methods = $"Linked to {phoneName} · pair or unpair on the phone";
        if (!companion.Linked)
        {
            return new DeviceRowViewModel(companion.Name, "Watch", methods,
                "Not found on the phone: open AccountabiliBuddy on the watch", DeviceSyncState.Unlinked, "Not found", Indented: true);
        }

        string last = companion.LastHeard is DateTimeOffset heard ? $"last update {Ago(now - heard)}" : "not heard from yet";
        DeviceSyncState state = companion.LastHeard is DateTimeOffset seen
            ? DeviceRowViewModel.StateFor(now - seen < TimeSpan.Zero ? TimeSpan.Zero : now - seen, TimeSpan.FromDays(1))
            : DeviceSyncState.Waiting;
        string stateText = state switch
        {
            DeviceSyncState.Behind => "Behind",
            DeviceSyncState.Waiting => "Waiting",
            _ => "Linked",
        };
        return new DeviceRowViewModel(companion.Name, "Watch", methods, last, state, stateText, Indented: true);
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (SyncNow is not null)
        {
            await SyncNow();
            Message = "Sent this PC's sync file.";
        }
    }

    [RelayCommand]
    private void ShowQrCode()
    {
        ShowSyncQr?.Invoke();
    }

    /// <summary>Sync files or pictures picked in the view's file dialog.</summary>
    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (Import is null || paths.Count == 0)
        {
            return;
        }

        List<string> files = paths.Where(path => path.EndsWith(SyncFolderService.Extension, StringComparison.OrdinalIgnoreCase)).ToList();
        var results = new List<string>();
        foreach (string file in files)
        {
            results.Add(await Import.ImportFileAsync(file));
        }

        List<string> pictures = paths.Except(files).ToList();
        if (pictures.Count > 0)
        {
            results.Add(await Import.ImportPicturesAsync(pictures));
        }

        Message = string.Join(" · ", results.Distinct());
    }

    /// <summary>Shows this group's link code and its QR code (a new group first, when this PC isn't in one).</summary>
    [RelayCommand]
    private async Task LinkDeviceAsync()
    {
        if (_identity.Key is null)
        {
            if (IsLocked)
            {
                Message = LockedMessage;
                return;
            }

            await _identity.CreateGroupAsync();
        }

        string? code = _identity.CodeForDisplay();
        LinkCodeText = code;
        OnPropertyChanged(nameof(HasLinkCode));
        LinkQrImage = code is null ? null : Render($"accountabilibuddy://join?c={code}&n={Uri.EscapeDataString(_identity.DeviceName)}{RelayJoinParameters()}");
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        if (IsLocked)
        {
            Message = LockedMessage;
            return;
        }

        Message = await _identity.JoinAsync(JoinCode.Trim())
            ? "Linked to your other devices."
            : "That isn't a link code (40 characters, from Devices on your other device).";
        JoinCode = string.Empty;
    }

    [RelayCommand]
    private async Task LeaveGroupAsync()
    {
        if (IsLocked)
        {
            Message = LockedMessage;
            return;
        }

        await _identity.LeaveAsync();
        LinkCodeText = null;
        LinkQrImage = null;
        OnPropertyChanged(nameof(HasLinkCode));
        Message = "Left your device group: what came from the other devices is gone from this PC.";
    }

    /// <summary>The sync folder picked in the view; turns the folder method on.</summary>
    public Task SetFolderAsync(string path)
    {
        return ApplyAsync(settings => settings with { FolderPath = path, FolderOn = true });
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    partial void OnFolderOnChanged(bool value)
    {
        if (!_loading && value && FolderPath is null)
        {
            Message = "Choose the folder first.";
            Refresh();
            return;
        }

        _ = ApplyAsync(settings => settings with { FolderOn = value });
    }

    partial void OnQrOnChanged(bool value) => _ = ApplyAsync(settings => settings with { QrOn = value });

    partial void OnFileShareOnChanged(bool value) => _ = ApplyAsync(settings => settings with { FileShareOn = value });

    partial void OnDriveOnChanged(bool value)
    {
        if (!_loading && value && _driveAccount?.IsSignedIn != true)
        {
            Message = "Sign in to Google Drive first.";
            Refresh();
            return;
        }

        _ = ApplyAsync(settings => settings with { DriveOn = value });
    }

    [RelayCommand]
    private async Task SignInDriveAsync()
    {
        if (_driveAccount is null || OpenBrowser is null)
        {
            return;
        }

        try
        {
            Message = "Finish signing in in your browser…";
            await _driveAccount.SignInAsync(OpenBrowser, CancellationToken.None);
            await _settings.SaveAsync(_settings.Current with { DriveOn = true });
            Message = "Signed in: this PC now syncs through Google Drive.";
        }
        catch (GoogleSignInException ex)
        {
            Message = ex.Message;
        }
        catch (HttpRequestException)
        {
            Message = "Couldn't reach Google. Try again when you're online.";
        }
    }

    [RelayCommand]
    private async Task SignOutDriveAsync()
    {
        if (_driveAccount is null)
        {
            return;
        }

        if (IsLocked)
        {
            Message = LockedMessage;
            return;
        }

        await _settings.SaveAsync(_settings.Current with { DriveOn = false });
        await _driveAccount.SignOutAsync();
        Message = "Signed out of Google Drive. The files already there stay until you delete them in Drive.";
    }

    partial void OnRelayOnChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        if (!value)
        {
            _ = ApplyAsync(settings => settings with { RelayOn = false });
            return;
        }

        // Switching on takes the address and pin as typed.
        _ = SaveRelaySettingsAsync(turnOn: true);
    }

    [RelayCommand]
    private Task SaveRelayAsync()
    {
        return SaveRelaySettingsAsync(turnOn: false);
    }

    private async Task SaveRelaySettingsAsync(bool turnOn)
    {
        if (IsLocked)
        {
            Message = LockedMessage;
            Refresh();
            return;
        }

        if (!RelayAddress.TryNormalize(RelayUrlText, out Uri? address))
        {
            Message = "The relay address must be an https address, e.g. https://192.168.1.20:8743";
            Refresh();
            return;
        }

        if (!RelayAddress.TryNormalizePin(RelayPinText, out string? pin))
        {
            Message = "The pin is the 43-character fingerprint the relay prints.";
            Refresh();
            return;
        }

        await _settings.SaveAsync(_settings.Current with
        {
            RelayOn = turnOn || _settings.Current.RelayOn,
            RelayUrl = RelayAddress.Format(address),
            RelayPin = pin,
        });
        Message = "Saved the relay address.";
    }

    [RelayCommand]
    private async Task TestRelayAsync()
    {
        if (_relay is null)
        {
            return;
        }

        RelayTestText = "Testing…";
        RelayTestText = await _relay.TestAsync(RelayUrlText, RelayPinText);
    }

    partial void OnIntervalIndexChanged(int value) => _ = ApplyAsync(settings => settings with { Interval = (SyncInterval)Math.Clamp(value, 0, 3) });

    partial void OnReminderAtWorkEndChanged(bool value) => _ = ApplyAsync(settings => settings with
    {
        ReminderAt = value ? null : TimeOnly.FromTimeSpan(ReminderAt ?? new TimeSpan(17, 0, 0)),
    });

    partial void OnReminderAtChanged(TimeSpan? value)
    {
        if (!ReminderAtWorkEnd && value is TimeSpan time)
        {
            _ = ApplyAsync(settings => settings with { ReminderAt = TimeOnly.FromTimeSpan(time) });
        }
    }

    partial void OnSnoozeIndexChanged(int value) => _ = ApplyAsync(settings => settings with
    {
        SnoozeMinutes = SnoozeChoices[Math.Clamp(value, 0, SnoozeChoices.Length - 1)],
    });

    private const string LockedMessage = "Locked while a focus session you started is running.";

    // A switch the user changed: saved, unless a focus session the user started is running (then it goes back).
    private async Task ApplyAsync(Func<SyncSettings, SyncSettings> change)
    {
        if (_loading)
        {
            return;
        }

        if (IsLocked)
        {
            Message = LockedMessage;
            Refresh();
            return;
        }

        await _settings.SaveAsync(change(_settings.Current));
    }

    private async Task UpdateTodayAsync(DateTimeOffset now)
    {
        TimeZoneInfo zone = _time.LocalTimeZone;
        DateTime localDay = TimeZoneInfo.ConvertTime(now, zone).Date;
        DateTimeOffset dayStart = new(localDay, zone.GetUtcOffset(localDay));
        DateTimeOffset dayEnd = new(localDay.AddDays(1), zone.GetUtcOffset(localDay.AddDays(1)));
        SyncFile own;
        try
        {
            own = await _coordinator.BuildOwnFileAsync();
        }
        catch (SqliteException)
        {
            return;
        }

        List<TimeRange> mine = own.Sessions
            .Select(session => new TimeRange(session.Start, session.End ?? own.Device.WrittenAt))
            .Where(range => range.To > dayStart && range.From < dayEnd && range.To > range.From)
            .ToList();
        IReadOnlyList<ReceivedSession> theirs = ReceivedSessions.In(_received.Devices, dayStart, dayEnd);
        // Time any device was in focus counts once.
        TimeSpan total = ReceivedSessions.Covered(mine.Concat(theirs.Select(session => new TimeRange(session.Start, session.End))), dayStart, dayEnd);
        int count = (mine.Count > 0 ? 1 : 0) + theirs.Select(session => session.DeviceId).Distinct().Count();
        CombinedTodayText = count > 1 ? $"Today: {Duration(total)} focused across {count} devices" : $"Today: {Duration(total)} focused";
    }

    // "&r=<address>&p=<pin>" when the own relay is on, so a device that scans the QR joins the relay too (the pin is not secret).
    private string RelayJoinParameters()
    {
        SyncSettings settings = _settings.Current;
        if (!settings.RelayOn || !RelayAddress.TryNormalize(settings.RelayUrl, out Uri? address))
        {
            return string.Empty;
        }

        string parameters = "&r=" + Uri.EscapeDataString(RelayAddress.Format(address));
        return RelayAddress.TryNormalizePin(settings.RelayPin, out string? pin) && pin is not null
            ? parameters + "&p=" + Uri.EscapeDataString(pin)
            : parameters;
    }

    private static List<string> OwnMethods(SyncSettings settings)
    {
        var methods = new List<string>();
        if (settings.FolderOn)
        {
            methods.Add(SyncDeviceCard.MethodFolder);
        }

        if (settings.QrOn)
        {
            methods.Add(SyncDeviceCard.MethodQr);
        }

        if (settings.FileShareOn)
        {
            methods.Add(SyncDeviceCard.MethodFile);
        }

        if (settings.DriveOn)
        {
            methods.Add(SyncDeviceCard.MethodDrive);
        }

        if (settings.RelayOn)
        {
            methods.Add(SyncDeviceCard.MethodRelay);
        }

        return methods;
    }

    private static string MethodsText(IReadOnlyList<string> methods)
    {
        return methods.Count == 0 ? "no methods on" : string.Join(", ", methods.Select(MethodName));
    }

    private static string MethodName(string method)
    {
        return method switch
        {
            SyncDeviceCard.MethodLink => "network link (deleted)", // old files may still name it
            SyncDeviceCard.MethodFolder => "sync folder",
            SyncDeviceCard.MethodQr => "QR codes",
            SyncDeviceCard.MethodDrive => "Google Drive",
            SyncDeviceCard.MethodRelay => "Own relay",
            _ => "sync file",
        };
    }

    private static string KindName(string kind)
    {
        return kind switch
        {
            SyncDeviceCard.KindPc => "PC",
            SyncDeviceCard.KindWatch => "Watch",
            _ => "Phone",
        };
    }

    private static TimeSpan IntervalOf(SyncInterval interval)
    {
        return interval switch
        {
            SyncInterval.Instant => TimeSpan.FromMinutes(1),
            SyncInterval.OneMinute => TimeSpan.FromMinutes(1),
            SyncInterval.FiveMinutes => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(15),
        };
    }

    private static string Ago(TimeSpan age)
    {
        return age < TimeSpan.FromMinutes(1) ? "just now"
            : age < TimeSpan.FromHours(1) ? $"{(int)age.TotalMinutes} min ago"
            : age < TimeSpan.FromDays(1) ? $"{(int)age.TotalHours} h ago"
            : $"{(int)age.TotalDays} days ago";
    }

    private static string Duration(TimeSpan duration)
    {
        int minutes = (int)Math.Round(Math.Max(0, duration.TotalMinutes));
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m";
    }

    private static Bitmap Render(string text)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new Bitmap(new MemoryStream(new PngByteQRCode(data).GetGraphic(6)));
    }
}
