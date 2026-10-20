using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One app's own distance in Settings → On-screen timer (how far the timer moves down while it's maximized).</summary>
public sealed partial class AppDropRowViewModel : ObservableObject
{
    private readonly Action<AppDropRowViewModel> _remove;

    public AppDropRowViewModel(string app, int drop, int shift, Action<AppDropRowViewModel> remove)
    {
        App = app;
        Drop = drop;
        Shift = shift;
        _remove = remove;
    }

    /// <summary>Process name, e.g. "brave" or "winword".</summary>
    public string App { get; }

    /// <summary>Display pixels down; 0 = don't move down for this app.</summary>
    [ObservableProperty]
    public partial decimal? Drop { get; set; }

    /// <summary>Display pixels left of the corner (e.g. past a side panel).</summary>
    [ObservableProperty]
    public partial decimal? Shift { get; set; }

    [RelayCommand]
    private void Remove()
    {
        _remove(this);
    }
}
