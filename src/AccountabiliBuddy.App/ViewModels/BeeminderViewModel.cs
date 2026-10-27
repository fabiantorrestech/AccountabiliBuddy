using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Beeminder;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Beeminder, read only (ADR 43): the Integrations card (paste the personal auth token, test, remove) and Today's
/// "Beeminder" card (each goal with what's needed, safe days and the deadline). The token box is never filled back in.
/// </summary>
public sealed partial class BeeminderViewModel : ObservableObject
{
    private readonly BeeminderService _service;
    private readonly TimeProvider _time;

    public BeeminderViewModel(BeeminderService service, TimeProvider time)
    {
        _service = service;
        _time = time;
        TokenText = string.Empty;
        TokenPlaceholder = string.Empty;
        _service.Changed += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<BeeminderGoalRowViewModel> Goals { get; } = new();

    /// <summary>A token is saved: Today shows the card and Integrations offers Test and Remove.</summary>
    [ObservableProperty]
    public partial bool HasToken { get; set; }

    [ObservableProperty]
    public partial bool HasGoals { get; set; }

    /// <summary>The token box's hint: a saved token is never shown back.</summary>
    [ObservableProperty]
    public partial string TokenPlaceholder { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string TokenText { get; set; }

    /// <summary>"Updated 4:05 PM", or why the last read failed.</summary>
    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    /// <summary>The result of Save / Test / Remove on the Integrations card.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    private bool CanSave => !string.IsNullOrWhiteSpace(TokenText);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        Message = "Asking Beeminder…";
        string? problem = await _service.SaveTokenAsync(TokenText);
        if (problem is null)
        {
            TokenText = string.Empty;
            Message = $"Saved. {Goals.Count} goal(s) read; they show on Today.";
        }
        else
        {
            Message = problem;
        }
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        Message = "Asking Beeminder…";
        BeeminderRefresh result = await _service.RefreshAsync(userAsked: true);
        Message = result switch
        {
            BeeminderRefresh.Read => $"Works: {Goals.Count} goal(s) read.",
            BeeminderRefresh.NoToken => "No token saved: paste one first.",
            BeeminderRefresh.Busy => "Already asking Beeminder; try again in a moment.",
            BeeminderRefresh.Waiting => "Beeminder asked to wait; not asked again yet.",
            _ => _service.Problem ?? "That didn't work; try again.",
        };
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        await _service.RemoveAsync();
        Message = "Token removed. No more calls to Beeminder, and your goals left Today and your other devices.";
    }

    private void Refresh()
    {
        HasToken = _service.HasToken;
        TokenPlaceholder = HasToken ? "Token saved · paste a new one to replace it" : "Paste your personal auth token";
        DateTimeOffset now = _time.GetUtcNow();
        Goals.Clear();
        foreach (BeeminderGoal goal in _service.Goals)
        {
            Goals.Add(new BeeminderGoalRowViewModel(goal, now, _time.LocalTimeZone));
        }

        HasGoals = Goals.Count > 0;
        HasProblem = _service.Problem is not null;
        Status = _service.Problem
            ?? (_service.FetchedAt is DateTimeOffset fetched
                ? $"Updated {UpdatedText(TimeZoneInfo.ConvertTime(fetched, _time.LocalTimeZone).DateTime, TimeZoneInfo.ConvertTime(now, _time.LocalTimeZone).Date)}"
                : HasToken ? "Not read yet" : null);
    }

    private static string UpdatedText(DateTime fetched, DateTime today) =>
        fetched.Date == today ? fetched.ToString("h:mm tt") : fetched.ToString("ddd h:mm tt");
}
