using System.Windows.Input;
using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A row of the "This session is full" dialog: the place's number, the label's dot, its title (and "Step of …"), and the round ⇄ button.</summary>
public sealed class SessionFullSlotViewModel
{
    public SessionFullSlotViewModel(SessionSlotChoice choice, ICommand swap)
    {
        Choice = choice;
        SwapCommand = swap;
        Dot = TaskCategoryBrushes.Of(choice.LabelColor);
    }

    public SessionSlotChoice Choice { get; }

    public int Number => Choice.Number;

    public string Title => Choice.Title;

    public string Sub => Choice.Sub;

    public bool HasSub => Sub.Length > 0;

    public IBrush Dot { get; }

    public bool HasDot => Choice.LabelColor is not null;

    /// <summary>"Swap": this one goes to Queued (or out of focus) and the incoming one takes its number.</summary>
    public ICommand SwapCommand { get; }

    public string SwapTip => Choice.Slot?.StepId is not null ? "Swap: this step leaves focus" : "Swap: this one goes to Queued";
}
