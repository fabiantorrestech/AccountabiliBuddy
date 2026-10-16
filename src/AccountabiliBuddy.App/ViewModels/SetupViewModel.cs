using System.Collections.ObjectModel;
using System.Security;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Setup tab: every permission the app holds, each with Grant and Revoke.
/// Start with Windows (HKCU Run key) and one browser connection per supported browser.
/// Screen reading (M6) and activity logging (M7) are shown as upcoming.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly IStartupRegistrar _startup;
    private readonly IBrowserActivitySource _browsers;

    public SetupViewModel(IStartupRegistrar startup, IBrowserSetup browserSetup, IBrowserActivitySource browsers)
    {
        _startup = startup;
        _browsers = browsers;
        StartupStatus = string.Empty;

        foreach (BrowserKind browser in browserSetup.SupportedBrowsers)
        {
            Browsers.Add(new BrowserSetupRowViewModel(browserSetup, browser, () => _browsers.Connections));
        }

        _browsers.ConnectionsChanged += (_, _) => Dispatcher.UIThread.Post(RefreshBrowsers);

        RefreshStartup();
        RefreshBrowsers();
    }

    public ObservableCollection<BrowserSetupRowViewModel> Browsers { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantStartupCommand), nameof(RevokeStartupCommand))]
    public partial bool IsStartupGranted { get; set; }

    [ObservableProperty]
    public partial string StartupStatus { get; set; }

    private bool CanGrantStartup => !IsStartupGranted;

    private bool CanRevokeStartup => IsStartupGranted;

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

    [RelayCommand]
    private void RefreshAll()
    {
        RefreshStartup();
        RefreshBrowsers();
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
