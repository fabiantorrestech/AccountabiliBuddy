using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using AccountabiliBuddy.App.Services.Today;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Today sidebar (round 2, brief §4.2, CN §3 PC): Next up (a mini day bar and the next three parts of today), Today's goals,
/// Come back to, and chips (Timers, Lunch, Beeminder), arranged by Settings › Today (<see cref="TodayLayoutService"/>, a layout in a
/// session and one outside). It slides over This session's list (nothing pushes, CN §1 rule 2) and hides to a right-edge rail during
/// a session; Ctrl+\ toggles it on Today; outside a session it remembers open or hidden until the app restarts
/// (<see cref="TodaySidebarState"/>). Every part is the existing card's view model: nothing here holds data of its own, and nothing
/// in it is a guard (the lunch chip stays on outside ProductaBuddy, <see cref="TodayLayoutRules.Effective"/>).
/// </summary>
public sealed partial class TodaySidebarViewModel : ObservableObject
{
    private readonly TodayLayoutService _layout;
    private readonly TodaySidebarState _state;

    public TodaySidebarViewModel(SessionViewModel session, TodayLayoutService layout, TodaySidebarState state)
    {
        _layout = layout;
        _state = state;
        NextUp = session.NextUp;
        TodayGoals = session.Goal;
        Comebacks = session.Comebacks;
        Timers = session.Timers;
        Lunch = session.Lunch;
        Beeminder = session.Beeminder;
        CalendarToday = session.CalendarToday;
        Steps = session.Steps;
        Panels = [];
        Chips = [];

        _state.SessionChanged(session.IsSessionActive);
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.IsSessionActive))
            {
                _state.SessionChanged(session.IsSessionActive);
            }
            else if (e.PropertyName == nameof(SessionViewModel.Lunch))
            {
                // The old lunch card stops being watched, so a replaced one isn't kept alive by this sidebar.
                if (Lunch is not null)
                {
                    Lunch.PropertyChanged -= OnPartChanged;
                }

                Lunch = session.Lunch;
                Watch(Lunch);
                RefreshAvailability();
            }
        };
        _state.Changed += (_, _) => OnStateChanged();
        _layout.Changed += (_, _) =>
        {
            _state.SettingsChanged(_layout.Current.SidebarInSession, _layout.Current.SidebarOutside);
            Rebuild();
        };
        _state.SettingsChanged(_layout.Current.SidebarInSession, _layout.Current.SidebarOutside);

        foreach (INotifyPropertyChanged? part in new INotifyPropertyChanged?[] { NextUp, Lunch, Beeminder, CalendarToday, Steps })
        {
            Watch(part);
        }

        WatchItems(Timers?.Timers);
        WatchItems(Timers?.Alarms);
        WatchItems(Beeminder?.Goals);
        WatchItems(Comebacks?.Items);
        Rebuild();
    }

    // ---- The parts' own view models (the existing Today cards) ----

    public NextUpViewModel? NextUp { get; }

    /// <summary>
    /// Today's goals. Round 2 hook: for now today's deep work against the daily goal (<see cref="GoalCardViewModel"/>); the Goals page
    /// (round 2 R5) replaces this with the goals shown on Today (icon, name, sub-line, progress, Log or ring).
    /// </summary>
    public GoalCardViewModel? TodayGoals { get; }

    public ComebacksViewModel? Comebacks { get; }

    public TimersViewModel? Timers { get; }

    [ObservableProperty]
    public partial LunchCardViewModel? Lunch { get; set; }

    public BeeminderViewModel? Beeminder { get; }

    public CalendarTodayViewModel? CalendarToday { get; }

    public StepsCardViewModel? Steps { get; }

    // ---- What is drawn ----

    /// <summary>The panels, in the chosen order (the layout of the state the day is in now).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TodaySidebarPartViewModel> Panels { get; set; }

    /// <summary>The chips under the panels, in the chosen order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChips))]
    public partial IReadOnlyList<TodaySidebarPartViewModel> Chips { get; set; }

    public bool HasChips => Chips.Count > 0;

    /// <summary>
    /// Round 2 review (brief §4.2, strictest reading): outside ProductaBuddy the lunch chip stays in view on the rail while the sidebar
    /// is hidden there (a session's default), when today has a lunch to show (<see cref="TodayLayoutRules.LunchOnRail"/>).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowsLunchOnRail { get; set; }

    /// <summary>The rail's lunch chip: the Lunch part, its pop-up the same panel as in the sidebar.</summary>
    public TodaySidebarPartViewModel RailLunch => _railLunch ??= new TodaySidebarPartViewModel(this, TodayParts.Of(TodayPart.Lunch), TodayPartPlacement.Chip) { IsAvailable = true };

    private TodaySidebarPartViewModel? _railLunch;

    /// <summary>Slid over the list (else only the rail at the right edge shows).</summary>
    public bool IsOpen => _state.IsOpen;

    public bool ShowsRail => !_state.IsOpen;

    /// <summary>Open during a session with "Quiet": faded until the pointer or the keyboard is on it.</summary>
    public bool IsQuiet => _state.IsQuiet;

    /// <summary>The rail's vertical note: "hidden during session" in a session, else how to show it.</summary>
    public string RailNote => _state.InSession ? "hidden during session" : "Ctrl+\\ to show";

    /// <summary>The header's muted note.</summary>
    public string HeaderNote => _state.InSession ? "Ctrl+\\ hides it again" : "slides over, hides in a session";

    /// <summary>Opens Settings › Today scrolled to the sidebar's parts (set by the app).</summary>
    public Action? OpenSettings { get; set; }

    /// <summary>Opens the Plan page (Next up's "Plan ›"; set by the app).</summary>
    public Action? OpenPlan { get; set; }

    /// <summary>Ctrl+\ on Today, the header's › and the rail's ←.</summary>
    [RelayCommand]
    private void Toggle()
    {
        _state.Toggle();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        OpenSettings?.Invoke();
    }

    [RelayCommand]
    private void ShowPlan()
    {
        OpenPlan?.Invoke();
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(ShowsRail));
        OnPropertyChanged(nameof(IsQuiet));
        OnPropertyChanged(nameof(RailNote));
        OnPropertyChanged(nameof(HeaderNote));
        Rebuild();
    }

    // The parts of the state the day is in now, each where the layout puts it (the guard rule applied); a part whose card isn't there
    // (no Beeminder view model in a check, say) is left out.
    private void Rebuild()
    {
        var panels = new List<TodaySidebarPartViewModel>();
        var chips = new List<TodaySidebarPartViewModel>();
        foreach (TodayPartChoice choice in _layout.Parts(_state.InSession))
        {
            if (TodayParts.ByKey(choice.Key) is not { } info || !HasCard(info.Part))
            {
                continue;
            }

            TodayPartPlacement placement = _layout.EffectivePlacement(choice);
            if (placement == TodayPartPlacement.Off)
            {
                continue;
            }

            var part = new TodaySidebarPartViewModel(this, info, placement);
            (placement == TodayPartPlacement.Panel ? panels : chips).Add(part);
        }

        Panels = panels;
        Chips = chips;
        RefreshAvailability();
    }

    private bool HasCard(TodayPart part) => part switch
    {
        TodayPart.NextUp => NextUp is not null,
        TodayPart.Goals => TodayGoals is not null,
        TodayPart.Comebacks => Comebacks is not null,
        TodayPart.Timers => Timers is not null,
        TodayPart.Lunch => true,
        TodayPart.Beeminder => Beeminder is not null,
        TodayPart.CalendarChanges => CalendarToday is not null,
        TodayPart.DailySteps => Steps is not null,
        _ => false,
    };

    private void RefreshAvailability()
    {
        ShowsLunchOnRail = TodayLayoutRules.LunchOnRail(_state.IsOpen, _layout.IsProductaBuddy, Lunch is { IsVisible: true });
        foreach (TodaySidebarPartViewModel part in Panels.Concat(Chips))
        {
            part.IsAvailable = part.Part switch
            {
                TodayPart.NextUp => NextUp is { HasRows: true } || NextUp is { NowFraction: >= 0 },
                TodayPart.Lunch => Lunch is { IsVisible: true },
                TodayPart.Beeminder => Beeminder is { HasToken: true },
                TodayPart.CalendarChanges => CalendarToday is { HasLines: true },
                TodayPart.DailySteps => Steps is { IsVisible: true } or { IsHidden: true },
                _ => true,
            };
            part.ChipCount = part.Part switch
            {
                TodayPart.Timers => Count((Timers?.Timers.Count ?? 0) + (Timers?.Alarms.Count ?? 0)),
                TodayPart.Beeminder => Count(Beeminder?.Goals.Count(goal => goal.IsDueToday) ?? 0),
                TodayPart.Comebacks => Count(Comebacks?.Items.Count ?? 0),
                _ => string.Empty,
            };
        }
    }

    private static string Count(int count) => count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private void Watch(INotifyPropertyChanged? source)
    {
        if (source is not null)
        {
            source.PropertyChanged += OnPartChanged;
        }
    }

    private void OnPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshAvailability();
    }

    private void WatchItems(INotifyCollectionChanged? source)
    {
        if (source is not null)
        {
            source.CollectionChanged += (_, _) => RefreshAvailability();
        }
    }
}
