using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "More today" fold's header count: how many of the cards inside need a look. An unlock waiting or unlocked, a
/// Beeminder goal due today, What's new showing, and a day off taken each count once. Read-only; it follows those cards.
/// </summary>
public sealed partial class MoreTodayViewModel : ObservableObject
{
    private readonly SessionViewModel _session;
    private readonly AccountabiliBuddy.App.Services.Today.TodayLayoutService? _layout;

    /// <param name="session">The Today view model, with its cards already set.</param>
    /// <param name="layout">Settings › Today (its "Show More today" switch); null = the card always shows.</param>
    public MoreTodayViewModel(SessionViewModel session, AccountabiliBuddy.App.Services.Today.TodayLayoutService? layout = null)
    {
        _session = session;
        _layout = layout;
        if (layout is not null)
        {
            layout.Changed += (_, _) => OnPropertyChanged(nameof(ShowsCard));
        }

        session.Unlock.PropertyChanged += OnCardChanged;
        if (session.WhatsNew is { } whatsNew)
        {
            whatsNew.PropertyChanged += OnCardChanged;
        }

        if (session.DayOff is { } dayOff)
        {
            dayOff.PropertyChanged += OnCardChanged;
        }

        if (session.Beeminder is { } beeminder)
        {
            beeminder.PropertyChanged += OnCardChanged;
            beeminder.Goals.CollectionChanged += OnGoalsChanged;
        }

        Refresh();
    }

    /// <summary>How many cards in the fold need a look; 0 = none, so the header shows no count.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlert), nameof(AlertText), nameof(ShowsCard))]
    public partial int AlertCount { get; set; }

    public bool HasAlert => AlertCount > 0;

    /// <summary>
    /// The fold card is on the page (round 2 boards draw nothing under the session card): when Today's Options or Settings › Today
    /// turned it on, and whenever the unlock card has something to offer or show (a way out is never hidden). Off, a count inside no
    /// longer brings it back (TD1: hidden is hidden, nothing else moves).
    /// </summary>
    public bool ShowsCard => _layout is null || AccountabiliBuddy.App.Services.Today.TodayLayoutRules.ShowsMoreToday(_layout.Current.ShowMoreToday, _session.Unlock.IsVisible);

    public string AlertText => AlertCount.ToString();

    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        Refresh();
        OnPropertyChanged(nameof(ShowsCard));
    }

    private void OnGoalsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        int count = 0;
        if (_session.Unlock.IsWaiting || _session.Unlock.IsUnlocked)
        {
            count++;
        }

        if (_session.Beeminder is { HasToken: true } beeminder && beeminder.Goals.Any(goal => goal.IsDueToday))
        {
            count++;
        }

        if (_session.WhatsNew is { IsVisible: true })
        {
            count++;
        }

        if (_session.DayOff is { IsVisible: true })
        {
            count++;
        }

        AlertCount = count;
    }
}
