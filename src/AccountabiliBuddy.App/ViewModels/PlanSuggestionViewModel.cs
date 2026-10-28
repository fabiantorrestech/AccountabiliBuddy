using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One Plan helper suggestion with its two buttons: the action ("Move it", "Start"…) and "Leave it".</summary>
public sealed partial class PlanSuggestionViewModel
{
    private readonly Func<PlanSuggestionViewModel, Task> _act;
    private readonly Func<PlanSuggestionViewModel, Task> _leave;

    public PlanSuggestionViewModel(PlanSuggestion suggestion, string actionText, Func<PlanSuggestionViewModel, Task> act, Func<PlanSuggestionViewModel, Task> leave)
    {
        Suggestion = suggestion;
        ActionText = actionText;
        _act = act;
        _leave = leave;
    }

    public PlanSuggestion Suggestion { get; }

    public string Text => Suggestion.Text;

    public string ActionText { get; }

    [RelayCommand]
    private Task Act() => _act(this);

    [RelayCommand]
    private Task Leave() => _leave(this);
}
