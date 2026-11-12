using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Today sidebar (<see cref="TodaySidebarViewModel"/>). Code-behind: the slide follows the Motion setting (Off: it shows and hides
/// at once; the slide is otherwise 220 ms, eased), and editing a come-back takes the keyboard.
/// </summary>
public partial class TodaySidebarView : UserControl
{
    private Transitions? _panelTransitions;

    public TodaySidebarView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyMotion();
        Services.Motion.SpeedChanged += OnMotionChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Services.Motion.SpeedChanged -= OnMotionChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        ApplyMotion();
    }

    private void ApplyMotion()
    {
        _panelTransitions = Services.Motion.Gate(SidebarPanel, _panelTransitions);
    }

    // Editing a come-back: the box takes the keyboard as it opens, with the old text selected.
    private void OnComebackBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextBox { IsVisible: true } box)
        {
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    // Clicking away keeps the new text, like Enter; Escape has already closed the box, so nothing is saved then.
    private void OnComebackBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ComebackRowViewModel { IsEditing: true } row })
        {
            row.SaveEditCommand.Execute(null);
        }
    }
}
