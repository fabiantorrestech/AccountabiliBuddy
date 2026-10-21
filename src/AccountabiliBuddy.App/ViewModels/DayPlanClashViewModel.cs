using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The day plan was changed on this PC and on another device before they synced (ADR 22): both edits with when each
/// was made (the latest marked) and what using the other device's plan changes, then "Keep the one I was using" or
/// "Use the latest". Closing the dialog without choosing uses the latest. Answers once.
/// </summary>
public sealed partial class DayPlanClashViewModel : ObservableObject
{
    private readonly Action<DayPlanClashChoice> _decide;
    private bool _decided;

    /// <param name="decide">Called once with the answer (see <see cref="SyncCoordinator.ResolveClashAsync"/>).</param>
    public DayPlanClashViewModel(DayPlanClashRequest request, TimeProvider time, Action<DayPlanClashChoice> decide)
    {
        _decide = decide;
        bool incomingIsLatest = ReferenceEquals(DayPlanClash.Latest(request.Local, request.Incoming), request.Incoming);
        LocalEdited = EditedText(request.Local.EditedAt, time, latest: !incomingIsLatest);
        IncomingTitle = $"On {request.FromDevice}";
        IncomingEdited = EditedText(request.Incoming.EditedAt, time, latest: incomingIsLatest);
        ChangesTitle = $"Using {request.FromDevice}'s plan changes:";
        Changes = request.Changes;
    }

    /// <summary>Raised when an answer was picked; the dialog closes.</summary>
    public event EventHandler? CloseRequested;

    public string LocalTitle => "On this PC";

    public string LocalEdited { get; }

    public string IncomingTitle { get; }

    public string IncomingEdited { get; }

    public string ChangesTitle { get; }

    public IReadOnlyList<string> Changes { get; }

    /// <summary>The dialog closed: unanswered, the latest edit wins.</summary>
    public void OnClosed()
    {
        Decide(DayPlanClashChoice.UseLatest);
    }

    [RelayCommand]
    private void KeepMine()
    {
        Decide(DayPlanClashChoice.KeepMine);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void UseLatest()
    {
        Decide(DayPlanClashChoice.UseLatest);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Decide(DayPlanClashChoice choice)
    {
        if (_decided)
        {
            return;
        }

        _decided = true;
        _decide(choice);
    }

    /// <returns>"edited 3:20 PM" today, else "edited Mon 10/19, 3:20 PM"; "· latest" on the newer one.</returns>
    private static string EditedText(DateTimeOffset editedAt, TimeProvider time, bool latest)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(editedAt, time.LocalTimeZone);
        DateTime today = TimeZoneInfo.ConvertTime(time.GetUtcNow(), time.LocalTimeZone).Date;
        string when = local.Date == today ? $"{local:t}" : $"{local:ddd M/d}, {local:t}";
        return latest ? $"edited {when} · latest" : $"edited {when}";
    }
}
