using AccountabiliBuddy.App.ViewModels;
using Avalonia;

namespace AccountabiliBuddy.App.Views;

/// <summary>"This session is full": swap one place out to Queued, or Add anyway (with the over-limit reason).</summary>
public partial class SessionFullDialog : TaskDialogWindow
{
    public SessionFullDialog()
    {
        InitializeComponent();
    }

    public SessionFullDialog(SessionFullViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }

    public static Task ShowAsync(Visual? opener, SessionFullViewModel viewModel)
    {
        return new SessionFullDialog(viewModel).ShowOverAsync(opener);
    }
}
