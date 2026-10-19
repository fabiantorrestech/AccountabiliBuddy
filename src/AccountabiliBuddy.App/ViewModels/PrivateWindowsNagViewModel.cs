using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The reminder to allow the extension in private windows (like Cold Turkey's). It lists each browser that needs it,
/// with steps. Closing it only snoozes it for a few minutes; it goes away for good once every browser is allowed.
/// </summary>
public sealed partial class PrivateWindowsNagViewModel : ObservableObject, IDisposable
{
    private readonly PrivateWindowAccessMonitor _monitor;
    private readonly IBrowserSetup _setup;

    public PrivateWindowsNagViewModel(PrivateWindowAccessMonitor monitor, IBrowserSetup setup)
    {
        _monitor = monitor;
        _setup = setup;
        _monitor.Changed += OnMonitorChanged;
        Rebuild();
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<PrivateWindowsNagRowViewModel> Browsers { get; } = new();

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public void Dispose()
    {
        _monitor.Changed -= OnMonitorChanged;
    }

    [RelayCommand]
    private void CheckAgain()
    {
        _monitor.RefreshNow();
        StatusMessage = _monitor.Missing.Count == 0
            ? null
            : "Still not allowed. Turn it on, then check again (the browser may need a moment to save it).";
    }

    [RelayCommand]
    private void RemindLater()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMonitorChanged(object? sender, EventArgs e)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        Browsers.Clear();
        foreach (BrowserKind browser in _monitor.Missing.OrderBy(browser => browser.ToString()))
        {
            Browsers.Add(new PrivateWindowsNagRowViewModel(_setup, browser, _setup.GetStatus(browser).DisplayName));
        }

        if (Browsers.Count == 0)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
