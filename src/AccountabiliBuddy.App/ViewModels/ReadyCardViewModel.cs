using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The planning "Ready?" card (in front of everything, like the away card): planning time is up. Start begins the focus
/// countdown (fresh, for the session's full length); +2, +5 and +15 give planning that many more minutes. Nothing here is
/// focus time, shortens a wait or ends the session.
/// </summary>
public sealed partial class ReadyCardViewModel
{
    private readonly ReadyCardActions _actions;

    /// <param name="taskName">The current task's name; empty for a card without one.</param>
    public ReadyCardViewModel(string taskName, ReadyCardActions actions)
    {
        _actions = actions;
        Title = taskName.Length > 0 ? $"Ready? Start on {taskName}" : "Ready?";
    }

    public string Title { get; }

    [RelayCommand]
    private void Start()
    {
        _actions.Start();
    }

    [RelayCommand]
    private void Extend(string minutes)
    {
        if (int.TryParse(minutes, out int extra))
        {
            _actions.Extend(extra);
        }
    }
}
