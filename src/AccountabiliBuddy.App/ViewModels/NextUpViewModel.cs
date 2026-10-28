using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Today's "Next up" card: the next three parts of today's plan that haven't ended (sleep left out). Read-only; it reads
/// the plan again when it changes and when the day bar's status line changes (the bar redraws every 30 seconds, so the
/// list moves on when a part's time passes). It reads the plan the
/// way the day bar does (counted focus and the night fallback), so Budget days match the bar.
/// </summary>
public sealed partial class NextUpViewModel : ObservableObject
{
    private const int MaxRows = 3;

    private readonly ConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly DayBarViewModel? _day;

    /// <param name="day">Today's bar, for the focus and night inputs a Budget day needs; null reads the plan without them.</param>
    public NextUpViewModel(ConfigurationService configuration, TimeProvider time, DayBarViewModel? day)
    {
        _configuration = configuration;
        _time = time;
        _day = day;
        Rows = [];
        Refresh();
        _configuration.Changed += (_, _) => Refresh();
        if (_day is not null)
        {
            _day.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DayBarViewModel.StatusText))
                {
                    Refresh();
                }
            };
        }
    }

    [ObservableProperty]
    public partial IReadOnlyList<NextUpRow> Rows { get; set; }

    [ObservableProperty]
    public partial bool HasRows { get; set; }

    private void Refresh()
    {
        DateTime now = _time.GetLocalNow().DateTime;
        var day = new LocalWindow(now.Date, now.Date.AddDays(1));
        var rows = DayWorkWindows.PartsIn(_configuration.Current.DayPlan, day, now, _day?.FocusedOn, _day?.NightFallback)
            .Where(part => part.Window.End > now && part.Category.Behavior != CategoryBehavior.Sleep)
            .Take(MaxRows)
            .Select(part => new NextUpRow(
                part.Window.Start <= now ? "Now" : Clock(part.Window.Start),
                $"{part.Name} until {Clock(part.Window.End)}"))
            .ToList();

        // Assigned only when the content differs, so the list isn't rebuilt for nothing.
        if (!rows.SequenceEqual(Rows))
        {
            Rows = rows;
        }

        HasRows = Rows.Count > 0;
    }

    private static string Clock(DateTime time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);
}
