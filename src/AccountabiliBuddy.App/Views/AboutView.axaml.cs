using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace AccountabiliBuddy.App.Views;

/// <summary>The About tab: the app, its version and updates. 5 taps on the logo open the migration assistant.</summary>
public partial class AboutView : UserControl
{
    private const int TapsToOpen = 5;
    private static readonly TimeSpan TapWindow = TimeSpan.FromSeconds(3);
    private int _taps;
    private DateTime _firstTap;

    public AboutView()
    {
        InitializeComponent();
    }

    private void OnLogoTapped(object? sender, TappedEventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        if (_taps == 0 || now - _firstTap > TapWindow)
        {
            _taps = 0;
            _firstTap = now;
        }

        if (++_taps >= TapsToOpen)
        {
            _taps = 0;
            (DataContext as UpdatesViewModel)?.OpenWhatsNew?.Invoke();
        }
    }
}
