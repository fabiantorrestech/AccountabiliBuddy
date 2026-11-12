using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Input;

namespace AccountabiliBuddy.App.Views;

/// <summary>Create a view / Edit view, from the Shelf's View menu. Enter in the name box saves.</summary>
public partial class ViewEditorDialog : TaskDialogWindow
{
    public ViewEditorDialog()
    {
        InitializeComponent();
    }

    public ViewEditorDialog(ViewEditorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }

    public static Task ShowAsync(Visual? opener, ViewEditorViewModel viewModel)
    {
        return new ViewEditorDialog(viewModel).ShowOverAsync(opener);
    }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ViewEditorViewModel editor)
        {
            editor.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }
}
