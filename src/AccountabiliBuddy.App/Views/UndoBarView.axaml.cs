using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Undo bar after a delete (Tasks page, Today's task rows).</summary>
public partial class UndoBarView : UserControl
{
    public UndoBarView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as UndoBarViewModel)?.SetAttached(true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as UndoBarViewModel)?.SetAttached(false);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        (DataContext as UndoBarViewModel)?.SetAttached(TopLevel.GetTopLevel(this) is not null);
    }
}
