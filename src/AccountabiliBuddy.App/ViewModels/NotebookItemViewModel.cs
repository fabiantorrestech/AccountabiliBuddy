using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line in the Notebook: a come-back, a note, or a task (unfinished or done), with its times.</summary>
/// <param name="Kind">"Come-back", "Note", "Unfinished task" or "Done task".</param>
/// <param name="WhenText">E.g. "Parked Thu 10/23 9:41 AM · ticked 2:05 PM" or "Session of Thu 10/23 9:00 AM · done 9:47 AM".</param>
/// <param name="Sort">For ordering, newest first.</param>
/// <param name="Tick">Ticks an open come-back off (null for everything else).</param>
public sealed record NotebookItemViewModel(string Kind, string Text, string WhenText, DateTimeOffset Sort, IAsyncRelayCommand? Tick = null)
{
    public bool CanTick => Tick is not null;
}
