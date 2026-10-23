using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

public partial class PreWorkWindow : Window
{
    public PreWorkWindow()
    {
        InitializeComponent();
        // The unlock code is typed by hand: pasting it would make the friction free.
        CodeBox.AddHandler(KeyDownEvent, OnCodeKeyDown, RoutingStrategies.Tunnel);
        CodeBox.PastingFromClipboard += (_, e) => e.Handled = true;
    }

    private void OnCodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
        {
            e.Handled = true;
        }
    }
}
