using AccountabiliBuddy.App.Services;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the shutdown card asks its service to do (set by the service).</summary>
/// <param name="Apply">Writes one step's choices when "Next" is pressed on it.</param>
/// <param name="Finish">Records "Work over for today" and closes the card.</param>
/// <param name="NotNow">Closes the card; it comes back once.</param>
/// <param name="OpenPlan">Opens the Plan page.</param>
public sealed record ShutdownCardActions(
    Func<ShutdownStep, ShutdownCardViewModel, Task> Apply,
    Func<ShutdownCardViewModel, Task> Finish,
    Func<Task> NotNow,
    Action OpenPlan);
