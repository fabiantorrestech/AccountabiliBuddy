using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A label's colour dot and its colour flyout (Labels screen). Code-behind: the flyout's content takes <see cref="Editor"/> as its data
/// context; the draft starts from <see cref="CurrentColor"/> when the flyout opens and is kept when it closes; Esc is seen on its way
/// down, before the flyout closes itself, so that closing keeps nothing.
/// </summary>
public partial class LabelColorButton : UserControl
{
    public static readonly StyledProperty<LabelColorEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<LabelColorButton, LabelColorEditorViewModel?>(nameof(Editor));

    public static readonly StyledProperty<IBrush?> DotProperty = AvaloniaProperty.Register<LabelColorButton, IBrush?>(nameof(Dot));

    public static readonly StyledProperty<string> CurrentColorProperty =
        AvaloniaProperty.Register<LabelColorButton, string>(nameof(CurrentColor), string.Empty);

    public static readonly StyledProperty<bool> IsDashedProperty = AvaloniaProperty.Register<LabelColorButton, bool>(nameof(IsDashed));

    public static readonly StyledProperty<string> TipProperty = AvaloniaProperty.Register<LabelColorButton, string>(nameof(Tip), "Change colour");

    private Flyout? _flyout;

    public LabelColorButton()
    {
        InitializeComponent();
    }

    /// <summary>The colour editor the flyout shows.</summary>
    public LabelColorEditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>The dot's colour.</summary>
    public IBrush? Dot
    {
        get => GetValue(DotProperty);
        set => SetValue(DotProperty, value);
    }

    /// <summary>The colour as stored, where the draft starts.</summary>
    public string CurrentColor
    {
        get => GetValue(CurrentColorProperty);
        set => SetValue(CurrentColorProperty, value);
    }

    /// <summary>A dashed outline (the new label row's dot).</summary>
    public bool IsDashed
    {
        get => GetValue(IsDashedProperty);
        set => SetValue(IsDashedProperty, value);
    }

    public string Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            EditorPanel.DataContext = Editor;
        }
    }

    private void OnEditorOpening(object? sender, EventArgs e)
    {
        _flyout = sender as Flyout;
        EditorPanel.DataContext = Editor;
        Editor?.Begin(CurrentColor);
    }

    private void OnEditorClosed(object? sender, EventArgs e)
    {
        Editor?.Finish();
    }

    private void OnEditorLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control editor)
        {
            editor.RemoveHandler(KeyDownEvent, OnEditorKeyDown);
            editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Editor?.Cancel();
            _flyout?.Hide();
            e.Handled = true;
        }
    }
}
