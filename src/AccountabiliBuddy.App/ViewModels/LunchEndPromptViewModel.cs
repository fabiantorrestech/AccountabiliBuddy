using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The small "End lunch?" question behind the ✕ on the Today lunch card, the title bar's lunch chip and the overlay's lunch row (LU1b, ADR 88): it
/// says how much of lunch is used and what is left, and offers "Count it" (the minutes count, the rest stays for later today), "Discard it" (as if it
/// never started; only when the active mode allows it) and "Keep going". Count and Discard bring a session that stepped away for lunch back, as
/// "I'm back" does. Every host (card, chip, overlay) has its own instance, so one opening never shows in the others. Times are 12-hour.
/// </summary>
public sealed partial class LunchEndPromptViewModel : ObservableObject
{
    private readonly LunchService _lunch;
    private readonly SessionController _sessions;

    public LunchEndPromptViewModel(LunchService lunch, SessionController sessions)
    {
        _lunch = lunch;
        _sessions = sessions;
        _sessions.Ticked += (_, _) =>
        {
            if (IsOpen)
            {
                Refresh();
            }
        };
        _lunch.Changed += (_, _) =>
        {
            if (IsOpen)
            {
                // Lunch ended or changed under the question (over, or ended on another device): nothing left to ask.
                if (!_lunch.IsOnLunch)
                {
                    Close();
                }
                else
                {
                    Refresh();
                }
            }
        };
        Refresh();
    }

    /// <summary>The question's title.</summary>
    public string Title => "End lunch?";

    /// <summary>The inline host (the Today card, the overlay) shows the question while this is true; a pop-up host ignores it.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>"Started 12:05 PM · 20 of 60 min used".</summary>
    [ObservableProperty]
    public partial string UsedText { get; set; } = string.Empty;

    /// <summary>"Count it: 20 min count, 40 min stay for later today."</summary>
    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    /// <summary>"Discard it: as if lunch never started."; empty when the mode does not allow discarding.</summary>
    [ObservableProperty]
    public partial string DiscardText { get; set; } = string.Empty;

    /// <summary>"Discard it" is offered (lunch runs and the mode's "Allow discarding a lunch" is on).</summary>
    [ObservableProperty]
    public partial bool CanDiscard { get; set; }

    /// <summary>Raised when the question is answered or dismissed (a pop-up host closes itself on it).</summary>
    public event EventHandler? Closed;

    /// <summary>Shows the question, read fresh.</summary>
    public void Open()
    {
        Refresh();
        IsOpen = true;
    }

    /// <summary>Closes the question without changing lunch.</summary>
    public void Close()
    {
        IsOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads lunch again: the minutes so far, what stays for later and whether Discard is offered.</summary>
    public void Refresh()
    {
        CanDiscard = _lunch.CanDiscard;
        if (_lunch.Taken is not SyncLunch lunch || !_lunch.IsOnLunch)
        {
            UsedText = string.Empty;
            CountText = string.Empty;
            DiscardText = string.Empty;
            return;
        }

        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        int allowance = LunchDay.AllowanceMinutes(lunch, (int)_lunch.Length.TotalMinutes);
        int used = (int)Math.Floor(_lunch.Used.TotalMinutes);
        int left = Math.Max(0, allowance - used);
        string started = TimeZoneInfo.ConvertTime(lunch.Start, zone).ToString("h:mm tt", CultureInfo.InvariantCulture);
        UsedText = $"Started {started} · {used} of {allowance} min used";
        CountText = left > 0
            ? $"Count it: {used} min count, {left} min stay for later today."
            : $"Count it: {used} min count.";
        DiscardText = CanDiscard ? "Discard it: as if lunch never started." : string.Empty;
    }

    [RelayCommand]
    private async Task CountItAsync()
    {
        await _lunch.CountAsync();
        Close();
    }

    [RelayCommand]
    private async Task DiscardItAsync()
    {
        // The lunch service checks the mode's setting again: a stale question can't discard when it was turned off in between.
        await _lunch.DiscardAsync();
        Close();
    }

    [RelayCommand]
    private void KeepGoing() => Close();
}
