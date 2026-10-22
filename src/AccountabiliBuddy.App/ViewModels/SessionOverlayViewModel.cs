using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The on-screen session timer: what's left of the current focus phase or break, in the chosen style. The bar and
/// ring show the part still to go, so they empty as time passes (or fill up with time passed, if chosen). A session
/// with no set end counts up: the bar fills over each hour and starts over, and every half hour it glows
/// (<see cref="HalfHourMarked"/>) with the time in shown in the middle for a few seconds. Near the end of a focus phase it can turn amber, passing a mark can give
/// one soft pulse, and holding Ctrl+Alt shows the details. Size, opacity and the drag outline belong to the window
/// it shares with the timeline (<see cref="OnScreenOverlayViewModel"/>).
/// </summary>
public sealed partial class SessionOverlayViewModel : ObservableObject
{
    public const double BarLength = 200;

    private static readonly Color BreakColor = Color.FromRgb(107, 158, 138);
    private static readonly Color WrapUpColor = Color.FromRgb(217, 119, 6);
    private static readonly TimeSpan WrapUpTime = TimeSpan.FromMinutes(5);

    /// <summary>The bar's colour at rest in quiet mode (<see cref="OverlaySettings.Quiet"/>).</summary>
    public static readonly Color QuietColor = Color.FromRgb(100, 116, 139);

    // The last minutes of a phase are loud: full colour even in quiet mode.
    private static readonly TimeSpan LoudTime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PulseLength = TimeSpan.FromMilliseconds(600);
    private const double PulseBrightness = 0.3;
    private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan BadgeHold = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _pulseTimer;

    // The phase the marks were last counted for, and how many marks it had passed.
    private DateTimeOffset? _countedPhaseStart;
    private int _marksPassed;

    // The count-up session the half hours were last counted for, and how many it had passed.
    private readonly DispatcherTimer _badgeTimer;
    private DateTimeOffset? _halfHoursStart;
    private int _halfHoursPassed;

    public SessionOverlayViewModel()
    {
        _pulseTimer = new DispatcherTimer { Interval = PulseLength };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulseTimer.Stop();
            PulseOpacity = 0;
        };

        _badgeTimer = new DispatcherTimer { Interval = BadgeHold };
        _badgeTimer.Tick += (_, _) =>
        {
            _badgeTimer.Stop();
            ShowsElapsedBadge = false;
        };

        HeaderLabel = HeaderTime = BigTimeText = EndText = RingText = ElapsedBadge = string.Empty;
        DetailTitle = DetailTimes = DetailProgress = string.Empty;
        AccentBrush = new SolidColorBrush(BreakColor);
    }

    [ObservableProperty]
    public partial bool IsRing { get; set; }

    [ObservableProperty]
    public partial bool ShowsHeader { get; set; }

    [ObservableProperty]
    public partial bool ShowsBigTime { get; set; }

    [ObservableProperty]
    public partial bool ShowsBar { get; set; }

    [ObservableProperty]
    public partial bool ShowsEndText { get; set; }

    /// <summary>E.g. "Coding" (shortened with … when long).</summary>
    [ObservableProperty]
    public partial string HeaderLabel { get; set; }

    /// <summary>E.g. "· 42:17 left", next to <see cref="HeaderLabel"/>.</summary>
    [ObservableProperty]
    public partial string HeaderTime { get; set; }

    /// <summary>E.g. "42:17 left".</summary>
    [ObservableProperty]
    public partial string BigTimeText { get; set; }

    /// <summary>E.g. "ends 3:30 PM" or "since 1:05 PM".</summary>
    [ObservableProperty]
    public partial string EndText { get; set; }

    [ObservableProperty]
    public partial string RingText { get; set; }

    /// <summary>Filled part of the bar, 0 to <see cref="BarLength"/>.</summary>
    [ObservableProperty]
    public partial double BarWidth { get; set; }

    /// <summary>Filled part of the ring in degrees.</summary>
    [ObservableProperty]
    public partial double RingSweep { get; set; }

    [ObservableProperty]
    public partial IBrush AccentBrush { get; set; }

    /// <summary>The last 2 minutes of a phase with an end: the window shows in full colour, even when quiet.</summary>
    [ObservableProperty]
    public partial bool IsLoud { get; set; }

    /// <summary>Marks splitting the bar or ring into equal parts; 0 = none.</summary>
    [ObservableProperty]
    public partial int Divisions { get; set; }

    /// <summary>Brightness of the pulse layer: 0 normally, briefly up when a mark is passed.</summary>
    [ObservableProperty]
    public partial double PulseOpacity { get; set; }

    /// <summary>Count-up: the time in (e.g. "1h 30m"), shown in the middle for a few seconds every half hour.</summary>
    [ObservableProperty]
    public partial string ElapsedBadge { get; set; }

    [ObservableProperty]
    public partial bool ShowsElapsedBadge { get; set; }

    /// <summary>Raised when a count-up session passes another half hour (the window glows).</summary>
    public event EventHandler? HalfHourMarked;

    /// <summary>The details below the timer, shown only while Ctrl+Alt is held.</summary>
    [ObservableProperty]
    public partial bool ShowsDetails { get; set; }

    /// <summary>E.g. "Coding" or "Break · Coding".</summary>
    [ObservableProperty]
    public partial string DetailTitle { get; set; }

    /// <summary>E.g. "9:00 AM – 12:00 PM" or "since 1:05 PM".</summary>
    [ObservableProperty]
    public partial string DetailTimes { get; set; }

    /// <summary>E.g. "2 of 4 parts done · 58% · 23m left", or "1h 12m so far" counting up.</summary>
    [ObservableProperty]
    public partial string DetailProgress { get; set; }

    public void SetDragReady(bool ready)
    {
        ShowsDetails = ready;
    }

    public void Update(SessionState state, DateTimeOffset now, TimeZoneInfo zone, string profileName, Color profileColor, OverlaySettings settings)
    {
        // Stepped away: everything reads the moment the countdown paused.
        now = state.CountdownTime(now);
        bool isBreak = !state.IsEnforcing;
        DateTimeOffset start = state.PhaseStartedAt ?? now;
        DateTimeOffset? end = state.PhaseEndsAt;

        // Part still to go; a session with no set end has nothing to empty (or fill).
        bool hasEnd = end is DateTimeOffset phaseEnd && phaseEnd > start;
        double remaining = hasEnd ? Math.Clamp((end!.Value - now) / (end.Value - start), 0, 1) : 0;

        // No set end (count-up): the bar fills over each hour and starts over.
        bool countsUp = end is null && !isBreak;
        TimeSpan elapsed = now > start ? now - start : TimeSpan.Zero;
        double shown = countsUp ? elapsed.TotalMinutes % 60 / 60
            : hasEnd && settings.FillUp ? 1 - remaining : remaining;
        string time = SessionStatusFormatter.FormatDuration(end is DateTimeOffset until ? until - now : now - start);
        string timeText = end is null ? $"{time} in" : $"{time} left";
        string label = isBreak ? "Break" : profileName;

        HeaderLabel = label;
        HeaderTime = $"· {timeText}";
        BigTimeText = isBreak ? $"Break · {timeText}" : timeText;
        RingText = time;
        EndText = end is DateTimeOffset ends
            ? $"{(isBreak ? "break ends" : "ends")} {TimeZoneInfo.ConvertTime(ends, zone):t}"
            : $"since {TimeZoneInfo.ConvertTime(start, zone):t}";

        // Rounded so the bar only redraws when it visibly moves.
        BarWidth = Math.Round(shown * BarLength * 2) / 2;
        RingSweep = Math.Round(shown * 360);

        IsLoud = hasEnd && end!.Value > now && end.Value - now <= LoudTime && !state.IsAway;
        Color accent = settings.Quiet && !ShowsDetails && !IsLoud ? QuietColor
            : isBreak ? BreakColor
            : settings.WrapUpColor && hasEnd && IsWrappingUp(start, end!.Value, now) ? WrapUpColor
            : profileColor;
        if (AccentBrush is not SolidColorBrush current || current.Color != accent)
        {
            AccentBrush = new SolidColorBrush(accent);
        }

        IsRing = settings.Style == OverlayStyle.Ring;
        ShowsHeader = settings.Style == OverlayStyle.TimeLeftWithBar;
        ShowsBigTime = settings.Style == OverlayStyle.TimeLeftOnly;
        ShowsBar = settings.Style is OverlayStyle.BarWithEndTime or OverlayStyle.TimeLeftWithBar;
        ShowsEndText = settings.Style == OverlayStyle.BarWithEndTime;
        Divisions = settings.Divisions;
        CountMarks(start, hasEnd ? 1 - remaining : null, settings);
        if (countsUp)
        {
            MarkHalfHours(start, elapsed);
        }

        DetailTitle = isBreak ? $"Break · {profileName}" : profileName;
        DetailTimes = end is DateTimeOffset finish
            ? $"{TimeZoneInfo.ConvertTime(start, zone):t} – {TimeZoneInfo.ConvertTime(finish, zone):t}"
            : $"since {TimeZoneInfo.ConvertTime(start, zone):t}";
        // Whole minutes left, rounded up, so the last minute reads "1m left" rather than "0m".
        string left = hasEnd ? $" · {Elapsed(TimeSpan.FromMinutes(Math.Ceiling(Math.Max(0, (end!.Value - now).TotalMinutes))))} left" : string.Empty;
        DetailProgress = countsUp ? $"{Elapsed(elapsed)} so far"
            : !hasEnd ? string.Empty
            : settings.Divisions >= 2 ? $"{_marksPassed} of {settings.Divisions} parts done · {(1 - remaining):0%}{left}"
            : $"{(1 - remaining):0%} done{left}";
    }

    /// <summary>The last 5 minutes, or the last fifth of a phase shorter than 25 minutes.</summary>
    private static bool IsWrappingUp(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        TimeSpan window = TimeSpan.FromTicks(Math.Min(WrapUpTime.Ticks, (end - start).Ticks / 5));
        return end - now <= window;
    }

    /// <summary>Counts the marks passed so far and pulses once when a new one is passed (not at the start or end).</summary>
    private void CountMarks(DateTimeOffset phaseStart, double? elapsed, OverlaySettings settings)
    {
        int divisions = settings.Divisions;
        int passed = elapsed is double done && divisions >= 2 ? Math.Min((int)Math.Floor(done * divisions), divisions) : 0;
        bool samePhase = _countedPhaseStart == phaseStart;

        if (samePhase && passed > _marksPassed && passed < divisions && settings.PulseAtMarks)
        {
            Pulse();
        }

        _countedPhaseStart = phaseStart;
        _marksPassed = passed;
    }

    /// <summary>Count-up: glows once for each half hour passed (not right after the window opens) and shows the time in.</summary>
    private void MarkHalfHours(DateTimeOffset sessionStart, TimeSpan elapsed)
    {
        int halfHours = (int)(elapsed / HalfHour);
        if (_halfHoursStart == sessionStart && halfHours > _halfHoursPassed)
        {
            ElapsedBadge = Elapsed(HalfHour * halfHours);
            ShowsElapsedBadge = true;
            _badgeTimer.Stop();
            _badgeTimer.Start();
            HalfHourMarked?.Invoke(this, EventArgs.Empty);
        }

        _halfHoursStart = sessionStart;
        _halfHoursPassed = halfHours;
    }

    /// <summary>"1h 30m" or "30m".</summary>
    private static string Elapsed(TimeSpan span)
    {
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m" : $"{span.Minutes}m";
    }

    private void Pulse()
    {
        PulseOpacity = PulseBrightness;
        _pulseTimer.Stop();
        _pulseTimer.Start();
    }
}
