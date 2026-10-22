using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>The window with today's sync QR codes; makes them when it opens.</summary>
public partial class SyncQrWindow : Window
{
    public SyncQrWindow()
    {
        InitializeComponent();
        Opened += (_, _) => (DataContext as SyncQrViewModel)?.LoadCommand.Execute(null);
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
