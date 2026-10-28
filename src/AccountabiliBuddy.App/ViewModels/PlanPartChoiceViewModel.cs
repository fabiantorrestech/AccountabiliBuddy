using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One day part in the "Put in plan" flyout, e.g. "3:00 PM · Deep work".</summary>
public sealed class PlanPartChoiceViewModel
{
    public PlanPartChoiceViewModel(string text, bool isSelected, Func<Task> pick)
    {
        Text = text;
        IsSelected = isSelected;
        PickCommand = new AsyncRelayCommand(() => pick());
    }

    public string Text { get; }

    /// <summary>The part the task is planned for now.</summary>
    public bool IsSelected { get; }

    public ICommand PickCommand { get; }
}
