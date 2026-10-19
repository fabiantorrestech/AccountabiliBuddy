using AccountabiliBuddy.Core.Ports;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One browser in the private-windows reminder, with its steps and a button to its extensions page.</summary>
public sealed partial class PrivateWindowsNagRowViewModel
{
    private readonly IBrowserSetup _setup;

    public PrivateWindowsNagRowViewModel(IBrowserSetup setup, BrowserKind browser, string displayName)
    {
        _setup = setup;
        Browser = browser;
        DisplayName = displayName;
        Steps = PrivateWindowSteps.For(browser);
    }

    public BrowserKind Browser { get; }

    public string DisplayName { get; }

    public string Steps { get; }

    [RelayCommand]
    private void OpenExtensionsPage()
    {
        _setup.OpenExtensionsPage(Browser);
    }
}
