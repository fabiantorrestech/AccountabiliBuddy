using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Today's lunch card (flexible break, schema v8): "Start lunch" on any workday, then the time left, +5 / −5 min and "I'm back".</summary>
public sealed partial class LunchCardViewModel : ObservableObject
{
    private readonly LunchService _lunch;
    private readonly SessionController _sessions;

    public LunchCardViewModel(LunchService lunch, SessionController sessions)
    {
        _lunch = lunch;
        _sessions = sessions;
        Text = string.Empty;
        _lunch.Changed += (_, _) => Refresh();
        _sessions.Ticked += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Shown on a workday or a day with a flexible break, or while a lunch runs.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial bool CanStart { get; set; }

    [ObservableProperty]
    public partial bool IsOnLunch { get; set; }

    /// <summary>−5 min is enabled: the lunch ends more than a minute from now.</summary>
    [ObservableProperty]
    public partial bool CanShorten { get; set; }

    /// <summary>"Lunch · usually 11:00 AM – 3:00 PM · 1 h", "Lunch · 42:10 left · until 1:05 PM" or "Lunch done · 12:05 PM – 1:05 PM".</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    public void Refresh()
    {
        FlexibleBreak? today = _lunch.Today;
        SyncLunch? taken = _lunch.Taken;
        IsOnLunch = _lunch.IsOnLunch;
        CanStart = _lunch.CanStart;
        CanShorten = _lunch.CanShorten;
        IsVisible = CanStart || taken is not null;
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        string length = LunchService.FormatLength(_lunch.Length);
        if (IsOnLunch && taken is not null)
        {
            TimeSpan left = taken.End - _sessions.Now;
            Text = $"Lunch · {(int)left.TotalMinutes}:{Math.Max(0, left.Seconds):00} left · until {TimeZoneInfo.ConvertTime(taken.End, zone):t}";
        }
        else if (taken is not null)
        {
            Text = $"Lunch done · {TimeZoneInfo.ConvertTime(taken.Start, zone):t} – {TimeZoneInfo.ConvertTime(taken.End, zone):t}";
        }
        else if (today is not null)
        {
            Text = $"{today.Name} · usually {today.Window.Start:t} – {today.Window.End:t} · {length}";
        }
        else
        {
            Text = $"Lunch · {length}, whenever you're ready";
        }
    }

    [RelayCommand]
    private Task StartAsync() => _lunch.StartAsync();

    [RelayCommand]
    private Task BackAsync() => _lunch.EndNowAsync();

    [RelayCommand]
    private Task LongerAsync() => _lunch.AdjustAsync(LunchService.AdjustStep);

    [RelayCommand]
    private Task ShorterAsync() => _lunch.AdjustAsync(-LunchService.AdjustStep);
}
