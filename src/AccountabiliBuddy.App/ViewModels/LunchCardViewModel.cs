using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Today's lunch card (flexible break, schema v8): "Start lunch" any time, then the time left and "I'm back".</summary>
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

    /// <summary>Shown on a day with a flexible break, or while a lunch runs.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial bool CanStart { get; set; }

    [ObservableProperty]
    public partial bool IsOnLunch { get; set; }

    /// <summary>"Lunch · any time 11:00 AM – 3:00 PM · 1h 00m", "Lunch · 42:10 left" or "Lunch done · 12:05 – 1:05 PM".</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    public void Refresh()
    {
        FlexibleBreak? today = _lunch.Today;
        SyncLunch? taken = _lunch.Taken;
        IsOnLunch = _lunch.IsOnLunch;
        CanStart = _lunch.CanStart;
        IsVisible = today is not null || taken is not null;
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
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
            Text = $"{today.Name} · any time {today.Window.Start:t} – {today.Window.End:t} · {SessionStatusFormatter.FormatDuration(today.Length)}";
        }
    }

    [RelayCommand]
    private Task StartAsync() => _lunch.StartAsync();

    [RelayCommand]
    private Task BackAsync() => _lunch.EndNowAsync();
}
