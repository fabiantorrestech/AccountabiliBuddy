using System.Collections.ObjectModel;
using System.Security;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Setup tab: every permission the app holds, each with Grant and Revoke.
/// Start with Windows (HKCU Run key), the quick-session Start menu shortcut, one browser connection per
/// supported browser, and the data permissions (screen reading, activity history) in <see cref="Privacy"/>.
/// During a focus phase every Revoke (and "Delete my data") is locked, so the session can't be loosened from here;
/// granting always works.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly IStartupRegistrar _startup;
    private readonly IQuickLaunchShortcut _quickLaunch;
    private readonly IBrowserActivitySource _browsers;

    public SetupViewModel(
        IStartupRegistrar startup,
        IQuickLaunchShortcut quickLaunch,
        IBrowserSetup browserSetup,
        IBrowserActivitySource browsers,
        PrivacySetupViewModel privacy,
        SessionController sessions,
        WatchedBrowsersService watchedBrowsers,
        PrivateWindowAccessMonitor privateAccess)
    {
        Privacy = privacy;
        _startup = startup;
        _quickLaunch = quickLaunch;
        _browsers = browsers;
        StartupStatus = QuickLaunchStatus = string.Empty;

        foreach (BrowserKind browser in browserSetup.SupportedBrowsers)
        {
            Browsers.Add(new BrowserSetupRowViewModel(browserSetup, browser, () => _browsers.Connections, watchedBrowsers, privateAccess.StatusOf));
        }

        privateAccess.Changed += (_, _) => RefreshBrowsers();

        watchedBrowsers.Changed += (_, _) => RefreshBrowsers();

        _browsers.ConnectionsChanged += (_, _) => Dispatcher.UIThread.Post(RefreshBrowsers);
        sessions.StateChanged += (_, _) => IsLocked = sessions.IsEnforcing;
        IsLocked = sessions.IsEnforcing;

        RefreshStartup();
        RefreshQuickLaunch();
        RefreshBrowsers();
    }

    public ObservableCollection<BrowserSetupRowViewModel> Browsers { get; } = new();

    public PrivacySetupViewModel Privacy { get; }

    /// <summary>Phones and tablets linked to this PC (ADR 14).</summary>
    public LinkedDevicesViewModel? LinkedDevices { get; init; }

    /// <summary>True during a focus phase: revoking and deleting are disabled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevokeStartupCommand), nameof(RevokeQuickLaunchCommand), nameof(RunAssistantCommand))]
    public partial bool IsLocked { get; set; }

    /// <summary>Opens the setup assistant; set by the app. Not available during a focus phase (it imports profiles).</summary>
    public Action? OpenAssistant { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantStartupCommand), nameof(RevokeStartupCommand))]
    public partial bool IsStartupGranted { get; set; }

    [ObservableProperty]
    public partial string StartupStatus { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantQuickLaunchCommand), nameof(RevokeQuickLaunchCommand))]
    public partial bool IsQuickLaunchGranted { get; set; }

    [ObservableProperty]
    public partial string QuickLaunchStatus { get; set; }

    private bool CanGrantQuickLaunch => !IsQuickLaunchGranted;

    private bool CanRevokeQuickLaunch => IsQuickLaunchGranted && !IsLocked;

    private bool CanGrantStartup => !IsStartupGranted;

    private bool CanRevokeStartup => IsStartupGranted && !IsLocked;

    partial void OnIsLockedChanged(bool value)
    {
        Privacy.IsLocked = value;
        foreach (BrowserSetupRowViewModel row in Browsers)
        {
            row.IsLocked = value;
        }
    }

    private bool CanRunAssistant => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanRunAssistant))]
    private void RunAssistant()
    {
        OpenAssistant?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanGrantStartup))]
    private void GrantStartup()
    {
        ChangeStartup(_startup.Enable);
    }

    [RelayCommand(CanExecute = nameof(CanRevokeStartup))]
    private void RevokeStartup()
    {
        ChangeStartup(_startup.Disable);
    }

    [RelayCommand(CanExecute = nameof(CanGrantQuickLaunch))]
    private void GrantQuickLaunch()
    {
        ChangeQuickLaunch(_quickLaunch.Install);
    }

    [RelayCommand(CanExecute = nameof(CanRevokeQuickLaunch))]
    private void RevokeQuickLaunch()
    {
        ChangeQuickLaunch(_quickLaunch.Remove);
    }

    [RelayCommand]
    private void RefreshAll()
    {
        RefreshStartup();
        RefreshQuickLaunch();
        RefreshBrowsers();
    }

    private void ChangeQuickLaunch(Action change)
    {
        try
        {
            change();
            RefreshQuickLaunch();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException)
        {
            QuickLaunchStatus = $"Could not change the shortcut: {ex.Message}";
        }
    }

    private void RefreshQuickLaunch()
    {
        IsQuickLaunchGranted = _quickLaunch.IsInstalled;
        QuickLaunchStatus = IsQuickLaunchGranted
            ? "\"AccountabiliBuddy Quick Session\" is in the Start menu. Right-click it there to pin it to the taskbar."
            : "Adds \"AccountabiliBuddy Quick Session\" to the Start menu, which you can pin to the taskbar.";
    }

    private void ChangeStartup(Action change)
    {
        try
        {
            change();
            RefreshStartup();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            StartupStatus = $"Could not change auto-start: {ex.Message}";
        }
    }

    private void RefreshStartup()
    {
        IsStartupGranted = _startup.IsEnabled;
        StartupStatus = IsStartupGranted
            ? "Starts in the tray when you sign in to Windows."
            : "Does not start with Windows. Scheduled blocks only run while the app is open.";
    }

    private void RefreshBrowsers()
    {
        IReadOnlyList<BrowserConnection> connections = _browsers.Connections;
        foreach (BrowserSetupRowViewModel row in Browsers)
        {
            row.Refresh(connections);
        }
    }
}
