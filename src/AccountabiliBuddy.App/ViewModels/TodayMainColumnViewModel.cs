using System.ComponentModel;
using AccountabiliBuddy.App.Services.Today;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Which of Today's two main parts are drawn (TD1): the session card (or start card) and This session's list, each hideable in the
/// layout of the state the day is in (Settings › Today). Read-only; it follows <see cref="TodayLayoutService"/> and the session. Hiding
/// is only about drawing: how to end or step away from a running session is the title bar's session control (and its ⋯ menu), which
/// reads none of this, and the day's notices, the guard banners and the lunch chip are not parts here.
/// </summary>
public sealed partial class TodayMainColumnViewModel : ObservableObject
{
    private readonly Func<bool> _isSessionActive;
    private readonly TodayLayoutService _layout;

    /// <param name="session">Raises PropertyChanged("IsSessionActive") when a session starts or ends (the Today view model).</param>
    /// <param name="isSessionActive">Whether a session runs now: picks which of the two layouts applies.</param>
    public TodayMainColumnViewModel(INotifyPropertyChanged session, Func<bool> isSessionActive, TodayLayoutService layout)
    {
        _isSessionActive = isSessionActive;
        _layout = layout;
        _layout.Changed += (_, _) => Refresh();
        session.PropertyChanged += OnSessionChanged;
        Refresh();
    }

    /// <summary>The session card (the ring and its buttons, or the start card) is drawn.</summary>
    [ObservableProperty]
    public partial bool ShowsSessionCard { get; set; } = true;

    /// <summary>This session's list (with Queued, Reminders and Finished under it) is drawn.</summary>
    [ObservableProperty]
    public partial bool ShowsThisSession { get; set; } = true;

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.IsSessionActive))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        bool inSession = _isSessionActive();
        ShowsSessionCard = !_layout.IsMainHidden(inSession, TodayMainParts.SessionCard);
        ShowsThisSession = !_layout.IsMainHidden(inSession, TodayMainParts.ThisSession);
    }
}
