using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The title-bar menu's dropdown (Tasks redesign 9). In its search: Enter or ↓ goes to the next match on the page (Enter: all results when the
/// page has none), ↑ or Shift+Enter to the previous one; Esc clears a typed search, and with nothing typed asks the window to close the menu.
/// </summary>
public partial class TitleMenuView : UserControl
{
    /// <summary>Esc with nothing typed, or a choice that leaves for another page: the window closes the menu.</summary>
    public event EventHandler? CloseRequested;

    public TitleMenuView()
    {
        InitializeComponent();
        Box.AddHandler(KeyDownEvent, OnBoxKeyDown, RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += (_, _) =>
        {
            ShowVariant();
            ApplyShadow();
        };
        // A theme picked in the menu swaps the app's tokens: the shadow's colour follows.
        ResourcesChanged += (_, _) => ApplyShadow();
    }

    /// <summary>Puts the caret in the search with its text selected (opening the menu, Ctrl+F).</summary>
    public void FocusSearch()
    {
        Box.Focus(NavigationMethod.Tab);
        Box.SelectAll();
    }

    /// <summary>Each time the menu opens: back to the top, the shadow in the theme's colour.</summary>
    public void Prepare()
    {
        Scroller.Offset = default;
        ApplyShadow();
    }

    // 9.04: shadow 0 20 50 in the theme's Ab.Shadow colour (a BoxShadows value can't take a dynamic colour in XAML).
    private void ApplyShadow()
    {
        Color color = this.TryFindResource("Ab.ShadowColor", ActualThemeVariant, out object? found) && found is Color shadow ? shadow : Colors.Transparent;
        Card.BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 20, Blur = 50, Spread = 0, Color = color });
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TitleMenuViewModel menu)
        {
            menu.CloseRequested -= OnMenuCloseRequested;
            menu.CloseRequested += OnMenuCloseRequested;
        }

        ShowVariant();
    }

    private void OnMenuCloseRequested(object? sender, EventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    // The swatches (and the theme view model's light/dark) follow what the window shows, Windows' own choice too while on System.
    private void ShowVariant()
    {
        if (DataContext is TitleMenuViewModel menu)
        {
            menu.ShowVariant(ActualThemeVariant == ThemeVariant.Dark);
        }
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TitleMenuViewModel { Search: { } search })
        {
            return;
        }

        if (search.HasPageMatches && (e.Key == Key.Up || (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Shift))))
        {
            search.PreviousCommand.Execute(null);
            e.Handled = true;
        }
        else if (search.HasPageMatches && e.Key == Key.Down)
        {
            search.NextCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            search.Submit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (search.HasQuery)
            {
                search.ClearCommand.Execute(null);
            }
            else
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }

            e.Handled = true;
        }
    }
}
