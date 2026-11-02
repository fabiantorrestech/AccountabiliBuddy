using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Tasks page's Categories editor. Code-behind: clicking away from a category's name keeps it, like Enter.</summary>
public partial class CategoryEditorView : UserControl
{
    public CategoryEditorView()
    {
        InitializeComponent();
    }

    private void OnCategoryNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TaskCategoryRowViewModel row })
        {
            row.SaveNameCommand.Execute(null);
        }
    }
}
