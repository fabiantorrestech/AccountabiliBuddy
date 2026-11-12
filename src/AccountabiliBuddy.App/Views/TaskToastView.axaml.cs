using System.ComponentModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media.Transformation;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Tasks toast (<see cref="TaskToastViewModel"/>) on the Tasks page and on Today. Code-behind: the timer bar follows the toast's
/// <see cref="TaskToastViewModel.Left"/> (a 0.1 s linear transition between the steps, none when animations are off), the toast fades
/// in when it appears, and the pointer resting on it holds its timer.
/// </summary>
public partial class TaskToastView : UserControl
{
    private static readonly TimeSpan BarStep = TimeSpan.FromMilliseconds(100);

    private TaskToastViewModel? _toast;

    public TaskToastView()
    {
        InitializeComponent();
        Card.PointerEntered += (_, _) => _toast?.SetHovered(true);
        Card.PointerExited += (_, _) => _toast?.SetHovered(false);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_toast is not null)
        {
            _toast.PropertyChanged -= OnToastPropertyChanged;
        }

        _toast = DataContext as TaskToastViewModel;
        if (_toast is not null)
        {
            _toast.PropertyChanged += OnToastPropertyChanged;
            ShowBar(_toast.Left);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _toast?.SetHovered(false);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnToastPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_toast is null)
        {
            return;
        }

        if (e.PropertyName == nameof(TaskToastViewModel.Left))
        {
            ShowBar(_toast.Left);
        }
        else if (e.PropertyName == nameof(TaskToastViewModel.Text) && _toast.IsVisible && IsEffectivelyVisible)
        {
            // A new toast (or one replacing another) fades and lifts in.
            Motion.Current?.Enter(Card);
        }
    }

    private void ShowBar(double left)
    {
        TimeSpan step = Motion.Current?.Scale(BarStep) ?? BarStep;
        if (step <= TimeSpan.Zero)
        {
            Bar.Transitions = null;
        }
        else if (Bar.Transitions is null)
        {
            Bar.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = step }];
        }

        TransformOperations.Builder builder = TransformOperations.CreateBuilder(1);
        builder.AppendScale(Math.Clamp(left, 0, 1), 1);
        Bar.RenderTransform = builder.Build();
    }
}
