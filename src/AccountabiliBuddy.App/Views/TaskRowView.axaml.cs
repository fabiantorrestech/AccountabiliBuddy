using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// One task row (<see cref="ViewModels.TaskListRowViewModel"/>). Code-behind: the ⋯ menu and the category picker open from the row
/// they belong to, and the drag handle hands its pointer to the section it sits in (<see cref="TaskSectionView"/>), which reorders.
/// </summary>
public partial class TaskRowView : UserControl
{
    public TaskRowView()
    {
        InitializeComponent();
    }

    // A ⋯ button: the menu is built from the row it belongs to each time it opens.
    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            TaskMenus.Show(button);
            e.Handled = true;
        }
    }

    // The category chip or "+ category" spot: the picker of ⋯ › Category. Handled here, so the click does not also open or tick the row.
    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            TaskMenus.ShowCategoryPicker(button);
            e.Handled = true;
        }
    }

    private TaskSectionView? Section => this.FindAncestorOfType<TaskSectionView>();

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control handle)
        {
            Section?.HandlePressed(handle, e);
        }
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
    {
        Section?.HandleMoved(e);
    }

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        Section?.HandleReleased(e);
    }

    private void OnHandleCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        Section?.EndDrag();
    }
}
