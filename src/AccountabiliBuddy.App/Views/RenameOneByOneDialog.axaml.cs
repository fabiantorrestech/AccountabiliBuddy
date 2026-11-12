using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>Rename one by one (spec 05 §12). Code-behind: Enter goes to the next task (Rename all on the last), Alt+←/→ step, the box keeps the keyboard with its text picked.</summary>
public partial class RenameOneByOneDialog : TaskDialogWindow
{
    public RenameOneByOneDialog()
    {
        InitializeComponent();
    }

    public RenameOneByOneDialog(RenameOneByOneViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RenameOneByOneViewModel.Index))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    NameBox.Focus();
                    NameBox.SelectAll();
                });
            }
        };
    }

    public static Task ShowAsync(Visual? opener, RenameOneByOneViewModel viewModel)
    {
        return new RenameOneByOneDialog(viewModel).ShowOverAsync(opener);
    }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not RenameOneByOneViewModel rename)
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            rename.Enter();
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right)
        {
            if (e.Key == Key.Left && rename.PreviousCommand.CanExecute(null))
            {
                rename.PreviousCommand.Execute(null);
            }
            else if (e.Key == Key.Right && rename.NextCommand.CanExecute(null))
            {
                rename.NextCommand.Execute(null);
            }

            e.Handled = true;
        }
    }
}
