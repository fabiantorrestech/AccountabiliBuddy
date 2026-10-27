using Avalonia.Controls;
using Avalonia.Input;

namespace AccountabiliBuddy.App.Views;

/// <summary>One monitor's dim layer behind a card. A click on it brings the card back in front (<see cref="Pressed"/>).</summary>
public partial class ScreenDimWindow : Window
{
    public ScreenDimWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? Pressed;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Pressed?.Invoke(this, EventArgs.Empty);
    }
}
