using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Today's lunch card (flexible break, schema v8): "Start lunch" on any workday, then the time left, +5 / −5 min, "I'm back" (counts the minutes so
/// far) and a ✕ that asks "End lunch?" (Count it, Discard it, Keep going; ADR 88). After a counted piece "Start the rest (40 min left)" starts the next.
/// </summary>
public sealed partial class LunchCardViewModel : ObservableObject
{
    private readonly LunchService _lunch;
    private readonly SessionController _sessions;

    public LunchCardViewModel(LunchService lunch, SessionController sessions)
    {
        _lunch = lunch;
        _sessions = sessions;
        Text = string.Empty;
        Prompt = new LunchEndPromptViewModel(lunch, sessions);
        TitleBarPrompt = new LunchEndPromptViewModel(lunch, sessions);
        OverlayPrompt = new LunchEndPromptViewModel(lunch, sessions);
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

    /// <summary>A counted or discarded piece left time: "Start the rest" shows.</summary>
    [ObservableProperty]
    public partial bool CanStartRest { get; set; }

    /// <summary>"Start the rest (40 min left)".</summary>
    [ObservableProperty]
    public partial string StartRestText { get; set; } = string.Empty;

    /// <summary>The title bar's lunch chip: "Lunch · 42 min left" while lunch runs.</summary>
    [ObservableProperty]
    public partial string ChipText { get; set; } = string.Empty;

    /// <summary>The "End lunch?" question the Today card shows under its ✕ (inline).</summary>
    public LunchEndPromptViewModel Prompt { get; }

    /// <summary>The "End lunch?" question the title bar's lunch chip shows in its pop-up.</summary>
    public LunchEndPromptViewModel TitleBarPrompt { get; }

    /// <summary>The "End lunch?" question the overlay panel's lunch row shows (inline).</summary>
    public LunchEndPromptViewModel OverlayPrompt { get; }

    /// <summary>−5 min is enabled: the lunch ends more than a minute from now.</summary>
    [ObservableProperty]
    public partial bool CanShorten { get; set; }

    /// <summary>
    /// "Lunch · usually 11:00 AM – 3:00 PM · 1 h", "Lunch starts at 12:00 PM by itself · until 1:00 PM" (a fixed lunch, only until its window ends),
    /// "Lunch · 42:10 left · until 1:05 PM" or "Lunch done · 12:05 PM – 1:05 PM".
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>
    /// While lunch runs and another device is around: "On all your devices" or "Not on Laptop yet · last heard 12:03 PM" (<see cref="LunchDelivery"/>);
    /// empty otherwise. Read from the latest file received from each device whenever the card refreshes: no timer of its own.
    /// </summary>
    [ObservableProperty]
    public partial string DeliveryText { get; set; } = string.Empty;

    /// <summary>The other active devices' latest files as lunch peers (watches and removed devices left out); set by the app.</summary>
    public Func<IReadOnlyCollection<LunchPeer>>? Peers { get; set; }

    public void Refresh()
    {
        FlexibleBreak? today = _lunch.Today;
        SyncLunch? taken = _lunch.Taken;
        IsOnLunch = _lunch.IsOnLunch;
        CanStart = _lunch.CanStart;
        CanStartRest = _lunch.CanStartTheRest;
        int leftMinutes = (int)Math.Ceiling(_lunch.Remaining.TotalMinutes);
        StartRestText = $"Start the rest ({leftMinutes} min left)";
        CanShorten = _lunch.CanShorten;
        IsVisible = CanStart || taken is not null;
        ChipText = IsOnLunch && taken is not null ? $"Lunch · {Math.Max(1, (int)Math.Ceiling((taken.End - _sessions.Now).TotalMinutes))} min left" : string.Empty;
        DeliveryText = IsOnLunch && taken is not null && Peers is not null
            ? LunchDelivery.Line(taken, Peers(), _sessions.Now, time => TimeZoneInfo.ConvertTime(time, _sessions.TimeProvider.LocalTimeZone).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)) ?? string.Empty
            : string.Empty;
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        string length = LunchService.FormatLength(_lunch.Length);
        if (IsOnLunch && taken is not null)
        {
            TimeSpan left = taken.End - _sessions.Now;
            Text = $"Lunch · {(int)left.TotalMinutes}:{Math.Max(0, left.Seconds):00} left · until {TimeZoneInfo.ConvertTime(taken.End, zone):t}";
        }
        else if (taken is { Discarded: true })
        {
            // Discarded: as if it never started, but it stays "taken" so a fixed lunch doesn't start by itself again (ADR 88).
            Text = $"Lunch discarded · {leftMinutes} min left for today";
        }
        else if (taken is not null && CanStartRest)
        {
            int usedMinutes = (int)Math.Floor(_lunch.Used.TotalMinutes);
            Text = $"Lunch · {usedMinutes} min counted · {leftMinutes} min left for later";
        }
        else if (taken is not null)
        {
            Text = taken.Counted is { Count: > 0 }
                ? $"Lunch done · {(int)Math.Round(_lunch.Used.TotalMinutes)} min in all"
                : $"Lunch done · {TimeZoneInfo.ConvertTime(taken.Start, zone):t} – {TimeZoneInfo.ConvertTime(taken.End, zone):t}";
        }
        else if (_lunch.AutoStartPending is FixedLunch waiting && _sessions.State.IsActive
                 && TimeZoneInfo.ConvertTime(_sessions.Now, zone).DateTime >= waiting.Window.Start)
        {
            // Lunch's time came during a session: it starts by itself once the session has ended, and ends with its window.
            Text = $"Lunch starts when your session ends · until {waiting.Window.End:t}";
        }
        else if (_lunch.AutoStartPending is FixedLunch pending)
        {
            Text = $"Lunch starts at {pending.Window.Start:t} by itself · until {pending.Window.End:t}";
        }
        else if (_lunch.IsFlexibleToday && _lunch.FixedToday is FixedLunch skipped)
        {
            Text = $"Lunch at {skipped.Window.Start:t} won't start by itself today · press Start lunch before you go";
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

    /// <summary>"Start the rest": the next piece of today's lunch, for what is left.</summary>
    [RelayCommand]
    private Task StartRestAsync() => _lunch.StartAsync();

    /// <summary>"I'm back": the same as "Count it", without asking.</summary>
    [RelayCommand]
    private Task BackAsync() => _lunch.EndNowAsync();

    /// <summary>The ✕: asks "End lunch?" (Count it, Discard it, Keep going) under the card's text.</summary>
    [RelayCommand]
    private void AskEnd()
    {
        if (Prompt.IsOpen)
        {
            Prompt.Close();
        }
        else if (_lunch.IsOnLunch)
        {
            Prompt.Open();
        }
    }

    [RelayCommand]
    private Task LongerAsync() => _lunch.AdjustAsync(LunchService.AdjustStep);

    [RelayCommand]
    private Task ShorterAsync() => _lunch.AdjustAsync(-LunchService.AdjustStep);
}
