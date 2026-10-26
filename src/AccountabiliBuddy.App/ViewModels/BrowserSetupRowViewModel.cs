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
    private const string ColdTurkeyHint = "A running Cold Turkey block may lock the extensions page.";

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

    /// <summary>
    /// What the browser's load dialog asks for, for the Copy path button: the unpacked extension folder for
    /// Chromium browsers, the full path of its manifest.json for Firefox ("Load Temporary Add-on…" picks a file).
    /// Null until the extension has been copied into the data folder.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallPath))]
    public partial string? InstallPath { get; set; }

    public bool HasInstallPath => InstallPath is not null;

    public bool HasInstallSteps => InstallSteps is not null;

    public void Refresh(IReadOnlyList<BrowserConnection> connections)
    {
        BrowserSetupStatus status = _setup.GetStatus(Browser);
        DisplayName = status.DisplayName;
        IsInstalled = status.IsInstalled;
        IsGranted = status.IsHostRegistered;
        // Known whenever the extension copy is in the data folder (installed earlier or before a restart).
        InstallPath = _setup.FindPreparedExtensionPath(Browser);

        StatusText = !status.IsInstalled ? "Not installed"
            : status.Problem is not null ? status.Problem
            : status.IsHostRegistered ? "Installed · allowed"
            : "Installed · not allowed yet (Grant)";
        if (status.SharesRegistrationWith is not null)
        {
            StatusText += $" · shared with {status.SharesRegistrationWith}";
        }

        BrowserConnection? connection = connections.FirstOrDefault(candidate => candidate.Browser == Browser);
        IsConnected = connection is not null;
        ConnectionText = connection is null
            ? "Not connected"
            : $"Connected · {connection.ExtensionVersion} · {connection.ActiveTabs.Count} window(s)";

        DescribePrivateWindows(connection);
    }

    private void DescribePrivateWindows(BrowserConnection? connection)
    {
        bool watched = _watched.Watched.Contains(Browser);

        // While the browser runs the extension reports it; otherwise the browser's settings are read.
        bool? allowed = connection?.PrivateWindowsAllowed ?? _privateAccess(Browser) switch
        {
            PrivateWindowAccess.Allowed => true,
            PrivateWindowAccess.NotAllowed => false,
            _ => null,
        };
        string blockNote = watched ? " Counts as distracting during focus until allowed." : string.Empty;

        // What watching means (unseen windows count as distracting) is said once, above the list.
        IsPrivateWindowsWarning = allowed == false;
        PrivateWindowsText = allowed switch
        {
            true => "Allowed in private windows",
            false => $"Not allowed in private windows.{blockNote} To allow it: {PrivateWindowSteps.For(Browser)}.",
            null when connection is not null => "Private windows: unknown. Press Install extension… again to update it.",
            _ when watched => "Watched",
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
            // InstallPath follows from the copy just made when Run refreshes the row.
            InstallSteps = DescribeSteps(guide);
            _setup.OpenExtensionsPage(Browser);
        });
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
                $"Open {guide.ExtensionsPageUrl}, click \"Load Temporary Add-on…\" and paste the path below as the file name.",
                "Firefox removes it when it restarts, so do this again after a restart.",
                ColdTurkeyHint);
        }

        return string.Join(Environment.NewLine,
            $"Open {guide.ExtensionsPageUrl}, turn on \"Developer mode\", click \"Load unpacked\" and paste the path below as the folder.",
            ColdTurkeyHint);
    }
}
