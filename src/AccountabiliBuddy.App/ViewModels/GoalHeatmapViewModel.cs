using System.Globalization;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The goal heatmap on the detail pane (round 2, change note §4a "Heatmap"): range buttons (1 month / 3 months / 6 months / Year)
/// and ‹ › (› off at the latest page; paged back, the title is just the dates), weeks starting on the Options' week start, month
/// labels centred over their columns, one staircase divider per month, today ringed in the accent and the selected day in ink, a
/// hover bubble ("Fri 10/30 · 0 min · missed"), hit / missed / under / today / paused cells (brief §3.8), the goal's colour as the
/// ramp. Never squashed: cells are a fixed size and the oldest weeks crop from the left, stepping the range down once more than
/// about a month would be cropped (<see cref="GoalHeatmapFit"/>), from the width its slot reports (<see cref="SetAvailableWidth"/>).
/// No day paging: the page always shows today; a click picks a day for the day panel.
/// </summary>
public sealed partial class GoalHeatmapViewModel : ObservableObject
{
    public const double CellSize = 15;
    public const double Gap = 3;
    public const double LabelWidth = 26;

    private Goal? _goal;
    private GoalSourceData? _data;
    private DateOnly _today;
    private GoalWeekStart _weekStart;
    private GoalHeatmapLabels _labels;
    private DateOnly? _end;
    private double _width = double.NaN;
    private IReadOnlyDictionary<DateOnly, GoalDayState> _states = new Dictionary<DateOnly, GoalDayState>();

    /// <summary>The range asked for (kept while the goal changes; the page shows a shorter one when it would crop too much).</summary>
    [ObservableProperty]
    public partial GoalHeatmapRange WantedRange { get; set; } = GoalHeatmapRange.SixMonths;

    /// <summary>The range shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOneMonth), nameof(IsThreeMonths), nameof(IsSixMonths), nameof(IsYear))]
    public partial GoalHeatmapRange Range { get; set; } = GoalHeatmapRange.SixMonths;

    public bool IsOneMonth => Range == GoalHeatmapRange.OneMonth;

    public bool IsThreeMonths => Range == GoalHeatmapRange.ThreeMonths;

    public bool IsSixMonths => Range == GoalHeatmapRange.SixMonths;

    public bool IsYear => Range == GoalHeatmapRange.Year;

    /// <summary>"Last 6 months", or the dates once paged back.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>"May – Nov 2026" at the latest page; empty once paged back (the title holds the dates).</summary>
    [ObservableProperty]
    public partial string? Dates { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial bool CanGoNext { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<HeatCell> Cells { get; set; } = [];

    [ObservableProperty]
    public partial int Columns { get; set; } = 1;

    [ObservableProperty]
    public partial IReadOnlyList<string>? RowLabels { get; set; }

    [ObservableProperty]
    public partial double RowLabelWidth { get; set; } = LabelWidth;

    [ObservableProperty]
    public partial IReadOnlyList<HeatmapLabelSpan> MonthLabels { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<HeatmapDivider> Dividers { get; set; } = [];

    [ObservableProperty]
    public partial DateOnly Today { get; set; }

    /// <summary>The month lines' strength (HM1; the one device-local choice, set by whoever shows this heatmap).</summary>
    [ObservableProperty]
    public partial GoalMonthLines MonthLines { get; set; } = GoalMonthLines.Normal;

    /// <summary>The day the day panel shows (today until a cell is clicked).</summary>
    [ObservableProperty]
    public partial DateOnly? SelectedDay { get; set; }

    /// <summary>The ramp's colour (the goal's).</summary>
    [ObservableProperty]
    public partial IBrush? RampBrush { get; set; }

    /// <summary>"Shade = minutes that day (15+ counts)".</summary>
    [ObservableProperty]
    public partial string LegendNote { get; set; } = string.Empty;

    /// <summary>Weeks cropped from the left to fit (for the checks and the tooltip).</summary>
    [ObservableProperty]
    public partial int CroppedWeeks { get; set; }

    /// <summary>A cell was clicked: the day panel shows that day.</summary>
    public event EventHandler<DateOnly>? DayPicked;

    /// <summary>The goal's day states the grid shows (the day panel reads the same).</summary>
    public IReadOnlyDictionary<DateOnly, GoalDayState> States => _states;

    /// <summary>Shows <paramref name="goal"/>; a new goal goes back to the latest page.</summary>
    public void Show(Goal goal, GoalSourceData data, DateOnly today, GoalWeekStart weekStart, GoalHeatmapLabels labels, bool sameGoal)
    {
        if (!sameGoal || _end is null || _end > today)
        {
            _end = today;
        }

        _goal = goal;
        _data = data;
        _today = today;
        _weekStart = weekStart;
        _labels = labels;
        Today = today;
        RampBrush = GoalIcons.Brush(goal.Color);
        LegendNote = Legend(goal);
        Rebuild();
    }

    /// <summary>The width the grid's slot has (its own width, never the window's); refits when the weeks that fit change.</summary>
    public void SetAvailableWidth(double width)
    {
        if (double.IsNaN(width) || width <= 0 || Math.Abs(width - _width) < 0.5)
        {
            return;
        }

        int before = ColumnsThatFit(_width);
        _width = width;
        if (ColumnsThatFit(width) != before)
        {
            Rebuild();
        }
    }

    /// <summary>Whole weeks that fit a width (fixed cells: <see cref="CellSize"/> + <see cref="Gap"/>).</summary>
    public int ColumnsThatFit(double width)
    {
        if (double.IsNaN(width))
        {
            return 27; // Before the first layout: six months' worth.
        }

        double labels = _labels == GoalHeatmapLabels.None ? 0 : LabelWidth;
        return Math.Max(1, (int)Math.Floor((width - labels - 3 + Gap) / (CellSize + Gap)));
    }

    [RelayCommand]
    private void SetRange(GoalHeatmapRange range)
    {
        WantedRange = range;
        Rebuild();
    }

    [RelayCommand]
    private void Previous()
    {
        if (_end is DateOnly end)
        {
            _end = GoalHeatmapGrid.Step(Range, end, -1, _today);
            Rebuild();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (_end is DateOnly end)
        {
            _end = GoalHeatmapGrid.Step(Range, end, 1, _today);
            Rebuild();
        }
    }

    [RelayCommand]
    private void PickDay(DateOnly day)
    {
        if (day > _today)
        {
            return;
        }

        SelectedDay = day;
        DayPicked?.Invoke(this, day);
    }

    private void Rebuild()
    {
        if (_goal is not Goal goal || _data is not GoalSourceData data || _end is not DateOnly end)
        {
            return;
        }

        GoalHeatmapFitResult fit = GoalHeatmapFit.Fit(WantedRange, end, _weekStart, ColumnsThatFit(_width));
        GoalHeatmapGrid grid = fit.Grid;
        Range = fit.Range;
        CroppedWeeks = fit.CroppedColumns;
        bool latest = end >= _today;
        CanGoNext = !latest;
        string dates = DatesText(grid.RangeStart, grid.End);
        Title = latest ? LastText(fit.Range) : dates;
        Dates = latest ? dates : null;

        DateOnly last = grid.End < _today ? grid.End : _today;
        _states = GoalCounting.Days(goal, data, grid.RangeStart, last, _today).ToDictionary(state => state.Day);
        var cells = new List<HeatCell>(grid.Columns * GoalHeatmapGrid.Rows);
        for (int column = 0; column < grid.Columns; column++)
        {
            for (int row = 0; row < GoalHeatmapGrid.Rows; row++)
            {
                DateOnly day = grid.DayAt(row, column);
                if (!grid.Shows(day))
                {
                    if (day > grid.End && latest && day > _today)
                    {
                        cells.Add(new HeatCell(row, column, 0, $"{GoalTexts.Date(day, _today)} · still to come", null, HeatCellMark.Future));
                    }

                    continue;
                }

                if (!_states.TryGetValue(day, out GoalDayState? state))
                {
                    continue;
                }

                cells.Add(Cell(goal, state, row, column));
            }
        }

        Cells = cells;
        Columns = grid.Columns;
        RowLabels = Labels(_labels, _weekStart);
        RowLabelWidth = _labels == GoalHeatmapLabels.None ? 0 : LabelWidth;
        MonthLabels = grid.Months.Select(month => new HeatmapLabelSpan(month.Label, month.CentreColumn)).ToList();
        Dividers = grid.Months.Where(month => month.HasDivider).Select(month => new HeatmapDivider(month.FirstColumn, month.FirstRow)).ToList();
    }

    private HeatCell Cell(Goal goal, GoalDayState state, int row, int column)
    {
        GoalStripCell kind = GoalStrip14.CellOf(goal, state, _today);
        int step = GoalHeat.Step(goal, state);
        int level = step switch
        {
            0 => 0,
            1 => 1,
            2 => 3,
            _ => 4,
        };
        HeatCellMark mark = kind.Kind switch
        {
            GoalStripCellKind.Missed => HeatCellMark.Hollow,
            GoalStripCellKind.Partial => HeatCellMark.Faint,
            GoalStripCellKind.Paused => HeatCellMark.Paused,
            GoalStripCellKind.Today => HeatCellMark.Today,
            _ => HeatCellMark.None,
        };
        double progress = kind.Kind == GoalStripCellKind.Today ? kind.Progress : 0;
        return new HeatCell(row, column, level, Tip(goal, state), state.Day, mark, progress);
    }

    /// <summary>"Fri 10/30 · 0 min · missed".</summary>
    private string Tip(Goal goal, GoalDayState state)
    {
        string value = goal.Kind == GoalKind.Habit || goal.Target.Unit == GoalUnit.Minutes
            ? GoalTexts.Minutes(state.Minutes > 0 || goal.Kind == GoalKind.Habit ? state.Minutes : state.Value)
            : state.HasValue ? GoalTexts.Value(state.Value, goal.Target.Unit) : "no value";
        return $"{GoalTexts.Date(state.Day, _today)} · {value} · {StatusWord(state)}";
    }

    private static string StatusWord(GoalDayState state)
    {
        return state.Status switch
        {
            GoalDayStatus.Counted or GoalDayStatus.CountedTickedItself => state.Hit ? "counted" : "logged",
            GoalDayStatus.NotCounted => "under the target",
            GoalDayStatus.Missed => "missed",
            GoalDayStatus.InProgress => "in progress",
            GoalDayStatus.Paused => "paused",
            _ => "not due",
        };
    }

    private static string Legend(Goal goal)
    {
        if (goal.Kind == GoalKind.Habit)
        {
            return GoalTexts.Threshold(goal) is double threshold ? $"Shade = minutes that day ({(int)threshold}+ counts)" : "Shade = done that day";
        }

        if (goal.Target.Unit == GoalUnit.Minutes)
        {
            return goal.Target is { Period: GoalPeriod.Day, Amount: double amount }
                ? $"Shade = minutes that day ({GoalTexts.Minutes(amount)}+ counts)"
                : "Shade = minutes that day";
        }

        return "Shade = the day's value";
    }

    private static IReadOnlyList<string>? Labels(GoalHeatmapLabels labels, GoalWeekStart weekStart)
    {
        if (labels == GoalHeatmapLabels.None)
        {
            return null;
        }

        DayOfWeek first = GoalPeriods.FirstDay(weekStart);
        return Enumerable.Range(0, 7)
            .Select(row =>
            {
                string name = ((DayOfWeek)(((int)first + row) % 7)).ToString()[..2];
                return labels == GoalHeatmapLabels.All || row % 2 == 1 ? name : string.Empty;
            })
            .ToList();
    }

    private static string LastText(GoalHeatmapRange range)
    {
        return range switch
        {
            GoalHeatmapRange.OneMonth => "Last month",
            GoalHeatmapRange.ThreeMonths => "Last 3 months",
            GoalHeatmapRange.SixMonths => "Last 6 months",
            _ => "Last year",
        };
    }

    /// <summary>"May – Nov 2026", "Nov 2025 – May 2026".</summary>
    private static string DatesText(DateOnly from, DateOnly to)
    {
        string From(string format) => from.ToString(format, CultureInfo.InvariantCulture);
        string to0 = to.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        return from.Year == to.Year ? $"{From("MMM")} – {to0}" : $"{From("MMM yyyy")} – {to0}";
    }
}
