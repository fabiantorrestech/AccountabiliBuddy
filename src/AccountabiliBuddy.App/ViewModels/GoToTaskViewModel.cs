using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Ctrl+G's small prompt (spec 03 §E): a task ID ("142", or "142b" for a step), Enter opens that task on the Tasks page; what went wrong
/// ("No task 142") shows under the box and the prompt stays. Esc closes it.
/// </summary>
public sealed partial class GoToTaskViewModel : ObservableObject
{
    private readonly Func<string, string?> _go;

    /// <param name="go">Opens the task with the typed ID: null when it did, else what to say.</param>
    public GoToTaskViewModel(Func<string, string?> go)
    {
        _go = go;
        Text = string.Empty;
        Problem = string.Empty;
    }

    /// <summary>The task opened, or Esc: the prompt closes.</summary>
    public event EventHandler? Done;

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; set; }

    public bool HasProblem => Problem.Length > 0;

    partial void OnTextChanged(string value) => Problem = string.Empty;

    [RelayCommand]
    private void Go()
    {
        if (Text.Trim().Length == 0)
        {
            return;
        }

        string? problem = _go(Text);
        if (problem is null)
        {
            Done?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Problem = problem;
        }
    }

    [RelayCommand]
    private void Close() => Done?.Invoke(this, EventArgs.Empty);
}
