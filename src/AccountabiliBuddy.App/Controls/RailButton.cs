using Avalonia;
using Avalonia.Controls;
using Material.Icons;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// One item of the 76-wide rail: a Material icon over a small label. The chosen one is <see cref="IsSelected"/> (shown as
/// the :selected pseudo-class); its look is in MainWindow.axaml, in the theme's tokens.
/// </summary>
public sealed class RailButton : Button
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<RailButton, string?>(nameof(Label));

    public static readonly StyledProperty<MaterialIconKind> IconProperty =
        AvaloniaProperty.Register<RailButton, MaterialIconKind>(nameof(Icon));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<RailButton, bool>(nameof(IsSelected));

    static RailButton()
    {
        IsSelectedProperty.Changed.AddClassHandler<RailButton>((button, e) => button.PseudoClasses.Set(":selected", e.NewValue is true));
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public MaterialIconKind Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    // Its own template (an icon over a label), not Fluent's button.
    protected override Type StyleKeyOverride => typeof(RailButton);
}
