using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Asks before a goal action (round 2 R5.4, <see cref="GoalConfirmRequest"/>): the question, what happens, Cancel (keyboard first, so Enter
/// never deletes by accident) and the action (red for Delete). When the action can't run now (a session runs), only Close shows. A note
/// the action returns (it waits until 2:14 PM) is shown before closing.
/// </summary>
public partial class GoalConfirmDialog : TaskDialogWindow
{
    private readonly GoalConfirmRequest? _request;
    private bool _done;

    public GoalConfirmDialog()
    {
        InitializeComponent();
    }

    public GoalConfirmDialog(GoalConfirmRequest request)
        : this()
    {
        _request = request;
        TitleText.Text = request.Title;
        BodyText.Text = request.Text;
        RunButton.Content = request.ActionText;
        RunButton.IsVisible = request.CanRun;
        RunButton.Classes.Set("primary", !request.IsDanger);
        RunButton.Classes.Set("danger", request.IsDanger);
        CancelButton.Content = request.CanRun ? "Cancel" : "Close";
    }

    public static Task ShowAsync(Visual? opener, GoalConfirmRequest request)
    {
        return new GoalConfirmDialog(request).ShowOverAsync(opener);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        if (_done)
        {
            Close();
            return;
        }

        if (_request is null || !_request.CanRun)
        {
            return;
        }

        RunButton.IsEnabled = false;
        string? note = await _request.Run();
        RunButton.IsEnabled = true;
        if (note is null)
        {
            Close();
            return;
        }

        _done = true;
        ResultText.Text = note;
        ResultText.IsVisible = true;
        RunButton.Content = "Done";
        RunButton.Classes.Set("danger", false);
        RunButton.Classes.Set("primary", true);
        CancelButton.IsVisible = false;
    }
}
