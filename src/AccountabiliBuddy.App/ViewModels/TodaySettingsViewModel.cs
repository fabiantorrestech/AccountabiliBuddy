using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Today;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings › Today (round 2, plan R2.3, brief §4.2, board PC-Settings-Today): two layouts, in a session and not; the main column
/// (always the session card or Start, and This session); the sidebar's parts, each Panel / Chip / Off, dragged ≡ (or Alt+↑/↓) into
/// order; the side column during and outside a session; and Show finished today. Saved at once in "today.layout" through
/// <see cref="TodayLayoutService"/>. It only changes what Today draws, so it is not locked during a session (like Appearance and the
/// phone's Today layout); the one rule it holds is that outside ProductaBuddy the lunch row can't be off.
/// </summary>
public sealed partial class TodaySettingsViewModel : ObservableObject
{
    private readonly TodayLayoutService _layout;
    private bool _loading;

    public TodaySettingsViewModel(TodayLayoutService layout)
    {
        _layout = layout;
        _layout.Changed += (_, _) => Load();
        Load();
    }

    /// <summary>"In a session" (0) or "Not in a session" (1, the default: the board shows it).</summary>
    public IReadOnlyList<string> StateChoices { get; } = ["In a session", "Not in a session"];

    [ObservableProperty]
    public partial int StateIndex { get; set; } = 1;

    public bool EditsInSession => StateIndex == 0;

    /// <summary>The sidebar's parts of the layout being edited, in order.</summary>
    public ObservableCollection<TodayPartRowViewModel> Parts { get; } = [];

    public IReadOnlyList<string> InSessionChoices { get; } = ["Hidden", "Quiet", "Shown"];

    [ObservableProperty]
    public partial int InSessionIndex { get; set; }

    public IReadOnlyList<string> OutsideChoices { get; } = ["Remember", "Open", "Rail"];

    [ObservableProperty]
    public partial int OutsideIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowFinishedToday { get; set; }

    [ObservableProperty]
    public partial bool ShowMoreToday { get; set; }

    /// <summary>"Any width", "1024 px", "1280 px"…: the window width from which the sidebar shows outside a session.</summary>
    public IReadOnlyList<string> MinWidthChoices { get; } = TodayLayoutPreferences.SidebarMinWidths
        .Select(width => width == 0 ? "Any width" : width.ToString(System.Globalization.CultureInfo.InvariantCulture) + " px")
        .ToList();

    [ObservableProperty]
    public partial int MinWidthIndex { get; set; }

    /// <summary>The More group's "Starting the day" line (board PC-Settings-Today); both rows open Settings › Your day.</summary>
    public string StartOfDayText { get; set; } = "Start-of-day assistant, Pre-Work and lunch";

    /// <summary>The More group's "End of the workday" line.</summary>
    public string EndOfDayText { get; set; } = "Shutdown routine and its reminder";

    /// <summary>The note under the lunch row's rule: which mode the app is in.</summary>
    public string ModeNote => _layout.IsProductaBuddy
        ? "ProductaBuddy mode: every part can be turned off."
        : "AccountabiliBuddy mode: Lunch always shows while a guard is on.";

    /// <summary>A group of the page should come into view (its search area name: "Today layout", "Today sidebar").</summary>
    public event EventHandler<string>? RevealRequested;

    /// <summary>Asks the page to bring a group into view (Today's Options › Arrange cards… / Sidebar items and order…).</summary>
    public void Reveal(string area) => RevealRequested?.Invoke(this, area);

    /// <summary>Opens another Settings section (Your day: how the day starts and ends); set by the app.</summary>
    public Action<SettingsSection>? OpenSection { get; set; }

    [RelayCommand]
    private void OpenYourDay() => OpenSection?.Invoke(SettingsSection.YourDay);

    /// <summary>Back to the board's layout, both states.</summary>
    [RelayCommand]
    private Task ResetAsync() => _layout.ResetAsync();

    /// <summary>Moves a part to <paramref name="index"/> in the layout being edited (a drag's drop or Alt+↑/↓).</summary>
    public Task MoveAsync(string key, int index) => _layout.MoveAsync(EditsInSession, key, index);

    /// <summary>Alt+↑ (-1) or Alt+↓ (+1) on a row.</summary>
    public Task MoveByAsync(string key, int delta)
    {
        int at = Parts.ToList().FindIndex(row => row.Key == key);
        return at < 0 ? Task.CompletedTask : MoveAsync(key, at + delta);
    }

    partial void OnStateIndexChanged(int value)
    {
        OnPropertyChanged(nameof(EditsInSession));
        Load();
    }

    partial void OnInSessionIndexChanged(int value)
    {
        if (!_loading && value is >= 0 and <= 2)
        {
            _ = _layout.SetSidebarInSessionAsync((TodaySidebarInSession)value);
        }
    }

    partial void OnOutsideIndexChanged(int value)
    {
        if (!_loading && value is >= 0 and <= 2)
        {
            _ = _layout.SetSidebarOutsideAsync((TodaySidebarOutside)value);
        }
    }

    partial void OnShowMoreTodayChanged(bool value)
    {
        if (!_loading)
        {
            _ = _layout.SetShowMoreTodayAsync(value);
        }
    }

    partial void OnMinWidthIndexChanged(int value)
    {
        if (!_loading && value >= 0 && value < TodayLayoutPreferences.SidebarMinWidths.Count)
        {
            _ = _layout.SetSidebarMinWidthAsync(TodayLayoutPreferences.SidebarMinWidths[value]);
        }
    }

    partial void OnShowFinishedTodayChanged(bool value)
    {
        if (!_loading)
        {
            _ = _layout.SetShowFinishedTodayAsync(value);
        }
    }

    // Reads the layout again (also after every change, so a refused Off snaps back).
    private void Load()
    {
        _loading = true;
        TodayLayoutPreferences current = _layout.Current;
        InSessionIndex = (int)current.SidebarInSession;
        OutsideIndex = (int)current.SidebarOutside;
        ShowFinishedToday = current.ShowFinishedToday;
        ShowMoreToday = current.ShowMoreToday;
        MinWidthIndex = Math.Max(0, TodayLayoutPreferences.SidebarMinWidths.ToList().IndexOf(current.SidebarMinWidth));
        bool isProductaBuddy = _layout.IsProductaBuddy;
        List<TodayPartRowViewModel> rows = _layout.Parts(EditsInSession)
            .Where(choice => TodayParts.ByKey(choice.Key) is not null)
            .Select(choice =>
            {
                TodayPartInfo info = TodayParts.ByKey(choice.Key)!;
                return new TodayPartRowViewModel(info, choice.Placement, TodayLayoutRules.CanTurnOff(info, isProductaBuddy), PlaceAsync);
            })
            .ToList();
        // The same rows as shown (the change came from one of them): keep them, so the control in use keeps the keyboard.
        bool same = rows.Count == Parts.Count
            && rows.Zip(Parts).All(pair => pair.First.Key == pair.Second.Key && pair.First.Placement == pair.Second.Placement && pair.First.CanTurnOff == pair.Second.CanTurnOff);
        if (!same)
        {
            Parts.Clear();
            foreach (TodayPartRowViewModel row in rows)
            {
                Parts.Add(row);
            }
        }

        OnPropertyChanged(nameof(ModeNote));
        _loading = false;
    }

    private async Task PlaceAsync(TodayPartRowViewModel row, TodayPartPlacement placement)
    {
        if (!await _layout.SetPlacementAsync(EditsInSession, row.Key, placement))
        {
            Load();
        }
    }
}
