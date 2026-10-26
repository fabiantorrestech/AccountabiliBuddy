using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>The spare key / Emergency release window (ADR 39). The key is typed by hand: pasting would skip writing it down.</summary>
public partial class ColdTurkeyKeyWindow : Window
{
    public ColdTurkeyKeyWindow()
    {
        InitializeComponent();
        foreach (TextBox box in new[] { SpareBox, EmergencyBox })
        {
            box.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            box.PastingFromClipboard += (_, e) => e.Handled = true;
        }

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ColdTurkeyKeyViewModel viewModel)
            {
                viewModel.Done += (_, _) => Close();
            }
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V) || (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Insert))
        {
            e.Handled = true;
        }
    }
}
