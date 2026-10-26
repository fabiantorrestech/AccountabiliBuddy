using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

public partial class BrowserSetupList : UserControl
{
    private static readonly TimeSpan CopiedFeedback = TimeSpan.FromSeconds(1.5);

    public BrowserSetupList()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Copies the extension's path for pasting into the browser's load dialog: the folder for "Load unpacked",
    /// manifest.json itself for Firefox's "Load Temporary Add-on…".
    /// </summary>
    private async void OnCopyPathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: BrowserSetupRowViewModel { InstallPath: { } path } } button
            || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(path);
        button.Content = "Copied";
        await Task.Delay(CopiedFeedback);
        button.Content = "Copy path";
    }
}
