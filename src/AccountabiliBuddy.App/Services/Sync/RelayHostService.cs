using System.Globalization;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Runs the sealed relay (ADR 46) on this PC: the "Run a relay on this PC" switch. While it is on, the relay program that
/// ships next to the app is started as a child process when the app starts (listening on all interfaces, HTTPS with its own
/// certificate) and ends with the app. If it ends by itself it is started again after 1 s, then 2 s, 4 s … up to 60 s
/// (a run that lasted a minute starts the waits over). When the port is taken by another program it stops after
/// <see cref="MaxPortTries"/> tries in a row and says "Port N is in use" (Devices); "Start", a new port or a restart tries again. Its data is kept in the app's data folder, so it survives updates.
/// Turning it on also points this PC's own "Own relay" method at it (https://127.0.0.1:port, with the relay's pin).
/// Nothing here touches focus sessions, profiles or any enforcement setting: it only runs a program, edits one firewall rule
/// and edits the sync settings, so it is allowed during a session. The relay's output (times, group prefixes and statuses,
/// never tokens or names) is kept as the last 50 lines for the Devices page. Use from the UI thread only.
/// </summary>
public sealed class RelayHostService : IDisposable
{
    private const int MaxLogLines = 50;
    private const int MaxErrorLength = 200;
    private const string ReadyMarker = " relay starting on ";
    private const int MaxPortTries = 3;
    private static readonly TimeSpan FirstRestartWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LastRestartWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StableRun = TimeSpan.FromSeconds(60);

    private readonly SyncSettingsService _settings;
    private readonly IRelayProcess _process;
    private readonly IRelayFirewall _firewall;
    private readonly string _executablePath;
    private readonly Queue<string> _log = new();

    private CancellationTokenSource? _restartWait;
    private TimeSpan _nextRestartWait = FirstRestartWait;
    private long _startedAtTick;
    private string? _runError;
    private int _portTakenRuns;
    private bool _stoppedByUser;
    private bool _useForOwnMethod;
    private bool _started;
    private bool _disposed;

    public RelayHostService(SyncSettingsService settings, IRelayProcess process, IRelayFirewall firewall, RelayHostFiles files, string executablePath)
    {
        _settings = settings;
        _process = process;
        _firewall = firewall;
        Files = files;
        _executablePath = executablePath;
    }

    /// <summary>Raised on the UI thread when the status, the log or the last change moved.</summary>
    public event EventHandler? Changed;

    public RelayHostFiles Files { get; }

    /// <summary>The switch: this PC runs the relay whenever the app runs (unless it was stopped for this run).</summary>
    public bool IsOn => _settings.Current.HostRelayOn;

    public bool IsRunning => _process.IsRunning;

    /// <summary>True from a crash until the next start: the relay will start again by itself.</summary>
    public bool IsRestarting => _restartWait is not null;

    public int Port => _settings.Current.HostRelayPort;

    /// <summary>Why the relay isn't running (the program is missing, it can't start, or the last error it printed); null when fine.</summary>
    public string? Problem { get; private set; }

    /// <summary>When the relay last stored or deleted a file, or null if not since it started.</summary>
    public DateTimeOffset? LastChange { get; private set; }

    /// <summary>The newest lines of the relay's output, oldest first.</summary>
    public IReadOnlyList<string> LogLines => _log.ToList();

    public bool ProgramExists => File.Exists(_executablePath);

    public bool HasFirewallRule => _firewall.HasRule(_executablePath, Port);

    /// <summary>Starts the relay when the switch is on. Called once when the app starts.</summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _process.OutputLine += (_, line) => Dispatcher.UIThread.Post(() => OnOutput(line));
        _process.ErrorLine += (_, line) => Dispatcher.UIThread.Post(() => OnError(line));
        _process.Exited += (_, code) => Dispatcher.UIThread.Post(() => OnExited(code));
        if (IsOn)
        {
            StartProcess();
        }
    }

    /// <summary>
    /// Turns the switch on and starts the relay. This PC's own Own relay method is pointed at it as soon as the relay has
    /// made its certificate (its pin). Whether the firewall rule was made is the caller's business: the relay runs either way.
    /// </summary>
    public async Task TurnOnAsync()
    {
        _useForOwnMethod = true;
        await _settings.SaveAsync(_settings.Current with { HostRelayOn = true });
        _stoppedByUser = false;
        _portTakenRuns = 0;
        StartProcess();
    }

    /// <summary>Turns the switch off and ends the relay. The Own relay method pointed at it is turned off too.</summary>
    public async Task TurnOffAsync()
    {
        StopProcess();
        SyncSettings current = _settings.Current;
        bool pointsHere = IsLoopbackAddress(current.RelayUrl);
        await _settings.SaveAsync(current with
        {
            HostRelayOn = false,
            RelayOn = pointsHere ? false : current.RelayOn,
            RelayUrl = pointsHere ? null : current.RelayUrl,
            RelayPin = pointsHere ? null : current.RelayPin,
            RelayAdmissionProtected = pointsHere ? null : current.RelayAdmissionProtected,
        });
        Raise();
    }

    /// <summary>Starts the relay again after "Stop" (the switch stays on).</summary>
    public void StartNow()
    {
        _stoppedByUser = false;
        _portTakenRuns = 0;
        StartProcess();
    }

    /// <summary>Ends the relay for now; it starts again with the next launch of the app or with "Start".</summary>
    public void StopNow()
    {
        _stoppedByUser = true;
        StopProcess();
        Raise();
    }

    public void Restart()
    {
        StopProcess();
        _stoppedByUser = false;
        _portTakenRuns = 0;
        StartProcess();
    }

    /// <summary>Saves the port and, if the relay runs, restarts it on the new one. Devices that joined by QR need the new address.</summary>
    public async Task SetPortAsync(int port)
    {
        await _settings.SaveAsync(_settings.Current with { HostRelayPort = port });
        if (_process.IsRunning)
        {
            // The Own relay address of this PC follows when the relay announces it is up.
            Restart();
        }
        else
        {
            Raise();
        }
    }

    public Task SetPublicNetworksAsync(bool alsoPublic)
    {
        return _settings.SaveAsync(_settings.Current with { HostRelayPublic = alsoPublic });
    }

    /// <summary>Makes (or remakes) the firewall rule; Windows asks for permission.</summary>
    public Task<FirewallResult> AllowFirewallAsync()
    {
        return _firewall.AllowAsync(_executablePath, Port, _settings.Current.HostRelayPublic);
    }

    public Task<FirewallResult> RemoveFirewallRuleAsync()
    {
        return _firewall.RemoveAsync(_executablePath, Port);
    }

    /// <summary>Deletes every stored sync file. The relay is stopped for the deletion (it keeps a list in memory) and started again.</summary>
    /// <returns>Null when done, else why not.</returns>
    public string? ClearStorage()
    {
        StopProcess();
        string? problem = Files.ClearStorage();
        if (IsOn && !_stoppedByUser)
        {
            StartProcess();
        }

        Raise();
        return problem;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _restartWait?.Cancel();
        _process.Dispose();
    }

    private void StartProcess()
    {
        if (_disposed || _process.IsRunning)
        {
            return;
        }

        CancelRestartWait();
        if (!ProgramExists)
        {
            Problem = "The relay program isn't next to the app. Reinstall or update AccountabiliBuddy.";
            Raise();
            return;
        }

        try
        {
            Directory.CreateDirectory(Files.DataDirectory);
            _process.Start(_executablePath, ["--urls", $"https://0.0.0.0:{Port}", "--data", Files.DataDirectory]);
            _startedAtTick = Environment.TickCount64;
            _runError = null;
            Problem = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ScheduleRestart("The relay couldn't start: " + ex.Message);
        }

        Raise();
    }

    private void StopProcess()
    {
        CancelRestartWait();
        _process.Stop();
    }

    private void OnOutput(string line)
    {
        if (_disposed)
        {
            return;
        }

        AddLog(line);
        if (_runError is null && IsPortTaken(line))
        {
            _runError = line.Trim().Length > MaxErrorLength ? line.Trim()[..MaxErrorLength] : line.Trim(); // Logged on stdout, not stderr.
        }

        if (line.Contains(ReadyMarker, StringComparison.Ordinal))
        {
            _ = PointOwnMethodAtRelayAsync();
        }
        else if (IsStoreOrDelete(line))
        {
            LastChange = DateTimeOffset.Now;
        }

        Raise();
    }

    private void OnError(string line)
    {
        if (_disposed || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        // The first line of an error is the useful one ("Failed to bind to address …"); the stack that follows is not kept.
        if (_runError is null)
        {
            string trimmed = line.Trim();
            _runError = trimmed.Length > MaxErrorLength ? trimmed[..MaxErrorLength] : trimmed;
        }
    }

    private void OnExited(int code)
    {
        if (_disposed)
        {
            return;
        }

        bool ranStably = Environment.TickCount64 - _startedAtTick >= StableRun.TotalMilliseconds;
        if (ranStably)
        {
            _nextRestartWait = FirstRestartWait;
            _portTakenRuns = 0;
        }

        string reason = $"The relay stopped (exit code {code}). {_runError}".TrimEnd();
        _portTakenRuns = !ranStably && IsPortTaken(_runError) ? _portTakenRuns + 1 : 0;
        if (_portTakenRuns >= MaxPortTries)
        {
            // Another program (a dev build, or the installed app) holds the port: trying again changes nothing.
            Problem = $"Port {Port} is in use. Close the program using it or choose another port, then press Start.";
        }
        else if (IsOn && !_stoppedByUser)
        {
            ScheduleRestart(reason);
        }
        else
        {
            Problem = reason;
        }

        Raise();
    }

    // What Kestrel (and Windows) say when the port is taken.
    private static bool IsPortTaken(string? error)
    {
        return error is not null
            && (error.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
                || error.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase)
                || error.Contains("Failed to bind", StringComparison.OrdinalIgnoreCase));
    }

    private void ScheduleRestart(string reason)
    {
        CancelRestartWait();
        TimeSpan wait = _nextRestartWait;
        _nextRestartWait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, LastRestartWait.Ticks));
        var source = new CancellationTokenSource();
        _restartWait = source;
        Task.Delay(wait, source.Token).ContinueWith(
            _ => Dispatcher.UIThread.Post(() =>
            {
                if (_restartWait == source)
                {
                    _restartWait = null;
                    StartProcess();
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
        Problem = $"{reason} Starting again in {wait.TotalSeconds:0} s.";
    }

    private void CancelRestartWait()
    {
        _restartWait?.Cancel();
        _restartWait = null;
    }

    // The relay has made its certificate and is about to listen: this PC's own Own relay method can use it.
    private async Task PointOwnMethodAtRelayAsync()
    {
        string? pin = Files.ReadPin();
        SyncSettings settings = _settings.Current;
        if (pin is null || !settings.HostRelayOn)
        {
            return;
        }

        // At turn-on this PC's method is switched on; afterwards it only follows a changed port or pin while it is still pointed here.
        bool follow = settings.RelayOn && IsLoopbackAddress(settings.RelayUrl);
        if (!_useForOwnMethod && !follow)
        {
            return;
        }

        _useForOwnMethod = false;
        string address = RelayAddress.Format(new Uri($"https://127.0.0.1:{settings.HostRelayPort}/"));
        // This PC runs the relay, so it knows the relay's admission key and keeps it to start the group (and to put in the join link).
        string? admission = Files.ReadAdmissionKey();
        if (settings.RelayOn && settings.RelayUrl == address && settings.RelayPin == pin && _settings.ReadRelayAdmission() == admission)
        {
            return;
        }

        await _settings.SaveAsync(settings with
        {
            RelayOn = true,
            RelayUrl = address,
            RelayPin = pin,
            RelayAdmissionProtected = _settings.ProtectRelayAdmission(admission),
        });
    }

    private void AddLog(string line)
    {
        _log.Enqueue(WithLocalTime(line));
        while (_log.Count > MaxLogLines)
        {
            _log.Dequeue();
        }
    }

    // The relay stamps lines in UTC ("2026-10-27T14:03:11Z"); people read local 12-hour time.
    private static string WithLocalTime(string line)
    {
        int space = line.IndexOf(' ');
        if (space > 0 && DateTimeOffset.TryParse(line[..space], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset stamp))
        {
            return stamp.ToLocalTime().ToString("h:mm:ss tt", CultureInfo.CurrentCulture) + line[space..];
        }

        return line;
    }

    // "<time> PUT    <group prefix> 200": a file was stored or deleted.
    private static bool IsStoreOrDelete(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4
            && parts[1] is "PUT" or "DELETE"
            && int.TryParse(parts[3], out int status)
            && status is >= 200 and < 300;
    }

    private static bool IsLoopbackAddress(string? text)
    {
        return RelayAddress.TryNormalize(text, out Uri? address) && (address.IsLoopback || address.Host == "localhost");
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
