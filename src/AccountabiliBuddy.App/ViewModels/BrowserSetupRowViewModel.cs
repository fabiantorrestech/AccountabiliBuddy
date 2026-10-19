using System.Security;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One browser in the Setup tab: grant/revoke its connection to the app (native messaging host registration),
/// install the extension, and show whether the extension is currently connected, whether it may run in private
/// windows, and whether the browser is watched (its unseen windows count as distracting during focus).
/// </summary>
public sealed partial class BrowserSetupRowViewModel : ObservableObject
{
    private const string ColdTurkeyHint =
        "If a Cold Turkey block is running, it may lock the extensions page; install when no block is active.";

    private readonly IBrowserSetup _setup;
    private readonly Func<IReadOnlyList<BrowserConnection>> _currentConnections;
    private readonly WatchedBrowsersService _watched;
    private readonly Func<BrowserKind, PrivateWindowAccess> _privateAccess;

    /// <param name="privateAccess">Private-window access read from the browser's settings, for when it isn't running.</param>
    public BrowserSetupRowViewModel(
        IBrowserSetup setup,
        BrowserKind browser,
        Func<IReadOnlyList<BrowserConnection>> currentConnections,
        WatchedBrowsersService watched,
        Func<BrowserKind, PrivateWindowAccess> privateAccess)
    {
        _setup = setup;
        _currentConnections = currentConnections;
        _watched = watched;
        _privateAccess = privateAccess;
        Browser = browser;
        DisplayName = browser.ToString();
        StatusText = ConnectionText = string.Empty;
    }

    public BrowserKind Browser { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantCommand), nameof(InstallExtensionCommand))]
    public partial bool IsInstalled { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevokeCommand))]
    public partial bool IsGranted { get; set; }

    /// <summary>Set by the Setup tab during a focus phase: Revoke is disabled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevokeCommand))]
    public partial bool IsLocked { get; set; }

    /// <summary>Registration state in words, including a repair hint when it is broken.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ConnectionText { get; set; }

    /// <summary>Private-window permission and watch state in words; null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrivateWindowsText))]
    public partial string? PrivateWindowsText { get; set; }

    /// <summary>True when the extension can't see private windows, so the text is a warning.</summary>
    [ObservableProperty]
    public partial bool IsPrivateWindowsWarning { get; set; }

    public bool HasPrivateWindowsText => PrivateWindowsText is not null;

    /// <summary>Install instructions, shown after "Install extension" is pressed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallSteps))]
    public partial string? InstallSteps { get; set; }

    [ObservableProperty]
    public partial string? InstallFolder { get; set; }

    public bool HasInstallSteps => InstallSteps is not null;

    public void Refresh(IReadOnlyList<BrowserConnection> connections)
    {
        BrowserSetupStatus status = _setup.GetStatus(Browser);
        DisplayName = status.DisplayName;
        IsInstalled = status.IsInstalled;
        IsGranted = status.IsHostRegistered;

        StatusText = !status.IsInstalled ? "Not installed on this computer."
            : status.Problem is not null ? status.Problem
            : status.IsHostRegistered ? "Allowed to talk to AccountabiliBuddy."
            : "Not allowed yet. Grant to let the extension talk to the app.";
        if (status.SharesRegistrationWith is not null)
        {
            StatusText += $" (Shared with {status.SharesRegistrationWith}: granting or revoking one affects both.)";
        }

        BrowserConnection? connection = connections.FirstOrDefault(candidate => candidate.Browser == Browser);
        IsConnected = connection is not null;
        ConnectionText = connection is null
            ? "Extension not connected"
            : $"Extension {connection.ExtensionVersion} connected · {connection.ActiveTabs.Count} window(s)";

        DescribePrivateWindows(connection);
    }

    private void DescribePrivateWindows(BrowserConnection? connection)
    {
        bool watched = _watched.Watched.Contains(Browser);
        string watchNote = watched
            ? " During focus, windows of this browser that the extension can't see count as distracting."
            : string.Empty;

        // While the browser runs the extension reports it; otherwise the browser's settings are read.
        bool? allowed = connection?.PrivateWindowsAllowed ?? _privateAccess(Browser) switch
        {
            PrivateWindowAccess.Allowed => true,
            PrivateWindowAccess.NotAllowed => false,
            _ => null,
        };
        string blockNote = watched ? " Until it's allowed, this browser counts as distracting during focus." : string.Empty;

        IsPrivateWindowsWarning = allowed == false;
        PrivateWindowsText = allowed switch
        {
            true => "Allowed in private windows, so your rules apply there too." + watchNote,
            false => $"Not allowed in private windows.{blockNote} To allow it: {PrivateWindowSteps.For(Browser)}.",
            null when connection is not null => "Private windows: unknown. Update the extension (Install extension… again)." + watchNote,
            _ when watched => "Watched (the extension connects when the browser is open)." + watchNote,
            _ => null,
        };
    }

    private bool CanGrant => IsInstalled;

    private bool CanRevoke => IsGranted && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanGrant))]
    private void Grant()
    {
        Run(() => _setup.Register(Browser));
    }

    [RelayCommand(CanExecute = nameof(CanRevoke))]
    private void Revoke()
    {
        Run(() =>
        {
            _setup.Unregister(Browser);
            _watched.ForgetUngranted(_setup);
        });
        InstallSteps = null;
    }

    [RelayCommand(CanExecute = nameof(CanGrant))]
    private void InstallExtension()
    {
        Run(() =>
        {
            // The extension is useless without the host registration, so grant it as part of installing.
            _setup.Register(Browser);
            ExtensionInstallGuide guide = _setup.PrepareExtensionInstall(Browser);
            InstallFolder = guide.Folder;
            InstallSteps = DescribeSteps(guide);
            _setup.OpenFolder(guide.Folder);
            _setup.OpenExtensionsPage(Browser);
        });
    }

    [RelayCommand]
    private void OpenInstallFolder()
    {
        if (InstallFolder is not null)
        {
            Run(() => _setup.OpenFolder(InstallFolder));
        }
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
                                       or System.ComponentModel.Win32Exception)
        {
            StatusText = $"That didn't work: {ex.Message}";
            return;
        }

        Refresh(_currentConnections());
    }

    private static string DescribeSteps(ExtensionInstallGuide guide)
    {
        if (guide.IsTemporary)
        {
            return string.Join(Environment.NewLine,
                $"1. In Firefox, open {guide.ExtensionsPageUrl} (it should have opened already).",
                "2. Click \"Load Temporary Add-on…\" and choose manifest.json in the folder below.",
                "3. Firefox removes temporary add-ons when it restarts, so repeat this after a restart (a signed, permanent install comes later).",
                ColdTurkeyHint);
        }

        return string.Join(Environment.NewLine,
            $"1. Open {guide.ExtensionsPageUrl} in the browser (it should have opened; if not, type it into the address bar).",
            "2. Turn on \"Developer mode\" (top right).",
            "3. Click \"Load unpacked\" and choose the folder below.",
            "4. This row shows \"connected\" within a few seconds.",
            ColdTurkeyHint);
    }
}
