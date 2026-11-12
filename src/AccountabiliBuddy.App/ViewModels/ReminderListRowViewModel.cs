using System.Windows.Input;
using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One reminder in the stack's list view ("All 6"): its label dot, the task's title (shown, never logged) and its time; a click shows its card.</summary>
public sealed class ReminderListRowViewModel
{
    public ReminderListRowViewModel(string title, IBrush dot, string time, bool isShown, ICommand open)
    {
        Title = title;
        Dot = dot;
        Time = time;
        IsShown = isShown;
        OpenCommand = open;
    }

    public string Title { get; }

    public IBrush Dot { get; }

    /// <summary>"2:50 PM".</summary>
    public string Time { get; }

    /// <summary>The card that shows when going back to the stack (tinted).</summary>
    public bool IsShown { get; }

    public ICommand OpenCommand { get; }
}
