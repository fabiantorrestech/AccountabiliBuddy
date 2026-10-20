using AccountabiliBuddy.App.Services;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Google setup guide (see <see cref="ViewModels.GoogleSetupViewModel"/>).</summary>
public partial class GoogleSetupWindow : Window
{
    private static readonly TimeSpan CopiedFeedback = TimeSpan.FromSeconds(1.5);

    public GoogleSetupWindow()
    {
        InitializeComponent();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GoogleSetupCopyValue copy } button || Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(copy.Value);
        button.Content = "Copied";
        await Task.Delay(CopiedFeedback);
        button.Content = "Copy";
    }
}
