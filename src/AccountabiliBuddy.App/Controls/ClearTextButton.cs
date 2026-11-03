using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using System;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The small "x" at the end of a search or filter box. Put it in the TextBox's InnerRightContent: it shows only while the box has
/// text, and one click empties the box (the bound search updates as if the text were deleted by hand) and keeps the cursor in it.
/// </summary>
public sealed class ClearTextButton : Button
{
    private TextBox? _box;
    private IDisposable? _textSubscription;

    public ClearTextButton()
    {
        Content = new MaterialIcon { Kind = MaterialIconKind.Close, Width = 14, Height = 14 };
        Background = Brushes.Transparent;
        Padding = new Thickness(5);
        MinWidth = 0;
        MinHeight = 0;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        // The box keeps the keyboard focus (and its Esc handling) while the x is used.
        Focusable = false;
        IsVisible = false;
        AutomationProperties.SetName(this, "Clear search");
        ToolTip.SetTip(this, "Clear search");
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _box = this.FindAncestorOfType<TextBox>();
        if (_box is null)
        {
            return;
        }
        _textSubscription = _box.GetObservable(TextBox.TextProperty).Subscribe(new TextObserver(this));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _textSubscription?.Dispose();
        _textSubscription = null;
        _box = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (_box is null)
        {
            return;
        }
        _box.Text = string.Empty;
        _box.Focus();
    }

    private sealed class TextObserver(ClearTextButton owner) : IObserver<string?>
    {
        public void OnNext(string? value) => owner.IsVisible = !string.IsNullOrEmpty(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
