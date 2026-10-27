using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

public partial class IntegrationsView : UserControl
{
    // Beeminder's page with the signed-in user's personal auth token; opened in the browser, not by the app (ADR 43).
    private const string BeeminderTokenPage = "https://www.beeminder.com/api/v1/auth_token.json";

    public IntegrationsView()
    {
        InitializeComponent();
    }

    private void OnGetBeeminderToken(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(BeeminderTokenPage) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No browser registered: the address is also in the card's text.
        }
    }
}
