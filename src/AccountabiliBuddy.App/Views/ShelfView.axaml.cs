using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Shelf's columns (round 2 R3). Code-behind: the Shelf's dialogs open over the window: Create / Edit view (from the Views and
/// Options menus) and the weekly tidy-up.
/// </summary>
public partial class ShelfView : UserControl
{
    private ShelfViewModel? _subscribed;

    public ShelfView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.ViewEditorRequested -= OnViewEditorRequested;
            _subscribed.TidyUpRequested -= OnTidyUpRequested;
        }

        _subscribed = DataContext as ShelfViewModel;
        if (_subscribed is not null)
        {
            _subscribed.ViewEditorRequested += OnViewEditorRequested;
            _subscribed.TidyUpRequested += OnTidyUpRequested;
        }
    }

    // "Create a view…" (null) or "Edit view…" from the Views or Options menu.
    private void OnViewEditorRequested(object? sender, Core.Domain.TaskView? view)
    {
        if (_subscribed?.ViewEditor(view) is { } editor)
        {
            _ = ViewEditorDialog.ShowAsync(this, editor);
        }
    }

    private void OnTidyUpRequested(object? sender, TidyUpViewModel tidy)
    {
        _ = TidyUpDialog.ShowAsync(this, tidy);
    }
}
