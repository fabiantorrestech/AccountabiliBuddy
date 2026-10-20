using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Checks for a newer version shortly after launch, every few hours and on request (ADR 12). By default it tells
/// the user once per version (a notice, and Settings → Updates) and installs when asked; with automatic updates on
/// it installs by itself. It never installs while a session runs: an automatic install waits for the session to end.
/// Dev builds and test copies never check. Use from the UI thread only.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string SettingsKey = "updates";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly IAppUpdater _updater;
    private readonly SessionController _sessions;
    private readonly SqliteJsonDocumentStore _store;
    private readonly Action<string, string> _notify;
    private readonly Func<bool> _isWindowShown;
    private readonly DispatcherTimer _timer;
    private string? _notifiedVersion;

    /// <param name="notify">Shows a notice (title, text) to the user.</param>
    /// <param name="isWindowShown">True while the main window is open (an automatic install then restarts it shown).</param>
    public UpdateService(
        IAppUpdater updater,
        SessionController sessions,
        SqliteJsonDocumentStore store,
        Action<string, string> notify,
        Func<bool> isWindowShown)
    {
        _updater = updater;
        _sessions = sessions;
        _store = store;
        _notify = notify;
        _isWindowShown = isWindowShown;
        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await CheckAsync(manual: false);
        };
        Status = updater.IsInstalled ? "Not checked yet." : "Updates work in the installed app, not in dev builds.";
    }

    public UpdateSettings Settings { get; private set; } = UpdateSettings.Default;

    public bool CanUpdate => _updater.IsInstalled;

    /// <summary>E.g. "Up to date." or "0.1.19 is ready to install."</summary>
    public string Status { get; private set; }

    /// <summary>The newer version found, or null.</summary>
    public string? AvailableVersion { get; private set; }

    public bool IsBusy { get; private set; }

    /// <summary>Raised when the status, the version found or the settings change.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Settings = Task.Run(() => _store.LoadAsync<UpdateSettings>(SettingsKey)).GetAwaiter().GetResult() ?? UpdateSettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = UpdateSettings.Default;
        }
    }

    public void Start()
    {
        if (!CanUpdate)
        {
            return;
        }

        _sessions.StateChanged += async (_, state) =>
        {
            // An automatic install that waited for the session goes ahead once it ends.
            if (!state.IsActive && Settings.AutoInstall && AvailableVersion is not null)
            {
                await InstallAsync(automatic: true);
            }
        };
        _timer.Start();
    }

    public async Task SetAutoInstallAsync(bool autoInstall)
    {
        Settings = Settings with { AutoInstall = autoInstall };
        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(SettingsKey, Settings);
    }

    /// <param name="manual">From Check now: report "up to date" too, and don't show a notice.</param>
    public async Task CheckAsync(bool manual)
    {
        if (!CanUpdate || IsBusy)
        {
            return;
        }

        SetState("Checking for updates…", busy: true);
        try
        {
            AvailableVersion = await _updater.CheckAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetState($"Couldn't check for updates ({ex.Message}).", busy: false);
            return;
        }

        string checkedAt = DateTime.Now.ToString("t");
        if (AvailableVersion is null)
        {
            SetState($"Up to date (checked {checkedAt}).", busy: false);
            return;
        }

        SetState($"Version {AvailableVersion} is ready to install.", busy: false);
        if (Settings.AutoInstall)
        {
            await InstallAsync(automatic: true);
        }
        else if (!manual && _notifiedVersion != AvailableVersion)
        {
            _notifiedVersion = AvailableVersion;
            _notify(
                "Update ready",
                $"AccountabiliBuddy {AvailableVersion} is ready. Install it from Settings → Updates. "
                + "You can also turn on automatic updates there.");
        }
    }

    /// <param name="automatic">Installed by itself (automatic updates on), not from the Install button.</param>
    public async Task InstallAsync(bool automatic)
    {
        if (!CanUpdate || IsBusy || AvailableVersion is null)
        {
            return;
        }

        if (_sessions.State.IsActive)
        {
            SetState(automatic
                ? $"Version {AvailableVersion} installs when the session ends."
                : $"Version {AvailableVersion} is ready. End or finish the session first, then install.", busy: false);
            return;
        }

        SetState($"Downloading {AvailableVersion}…", busy: true);
        try
        {
            await _updater.DownloadAsync(percent => Dispatcher.UIThread.Post(() => SetState($"Downloading {AvailableVersion}… {percent}%", busy: true)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetState($"Download failed ({ex.Message}). Try again later.", busy: false);
            return;
        }

        if (_sessions.State.IsActive)
        {
            // A session started while downloading.
            SetState($"Version {AvailableVersion} is downloaded and installs when the session ends.", busy: false);
            return;
        }

        SetState($"Installing {AvailableVersion} and restarting…", busy: true);
        _updater.InstallAndRestart(minimized: automatic && !_isWindowShown());
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    private void SetState(string status, bool busy)
    {
        Status = status;
        IsBusy = busy;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
