using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Material.Icons;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The rail a hidden column folds into: "Shelf · 10 tasks" on the right edge of the Tasks page (or "Today · 3 in focus" on the left
/// when Today is hidden). A click (the <see cref="Command"/>) shows the column again.
/// </summary>
public partial class ShelfRailView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ShelfRailView, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> SubProperty = AvaloniaProperty.Register<ShelfRailView, string>(nameof(Sub), string.Empty);

    public static readonly StyledProperty<MaterialIconKind> IconProperty =
        AvaloniaProperty.Register<ShelfRailView, MaterialIconKind>(nameof(Icon), MaterialIconKind.ArrowExpandLeft);

    public static readonly StyledProperty<string> TipProperty = AvaloniaProperty.Register<ShelfRailView, string>(nameof(Tip), string.Empty);

    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<ShelfRailView, ICommand?>(nameof(Command));

    public ShelfRailView()
    {
        InitializeComponent();
    }

    /// <summary>"Shelf" or "Today".</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>"10 tasks", "3 in focus".</summary>
    public string Sub
    {
        get => GetValue(SubProperty);
        set => SetValue(SubProperty, value);
    }

    /// <summary>The show arrow.</summary>
    public MaterialIconKind Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>"Show Shelf (Ctrl+\)".</summary>
    public string Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
