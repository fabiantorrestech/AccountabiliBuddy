using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>Theme, light or dark and colour (ADR 49). <see cref="IsCompact"/> leaves out the place and colour for the migration assistant.</summary>
public partial class AppearanceView : UserControl
{
    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<AppearanceView, bool>(nameof(IsCompact));

    public AppearanceView()
    {
        InitializeComponent();
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // The city picker closed with its page: let the city list go.
        (DataContext as AppearanceViewModel)?.ReleaseCities();
    }
}
