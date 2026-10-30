using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>The PC overlay settings (15c). Its DataContext is an <see cref="ViewModels.OverlaySettingsViewModel"/>; the Settings shell places it under Alerts &amp; on-screen.</summary>
public partial class OverlaySettingsView : UserControl
{
    public OverlaySettingsView()
    {
        InitializeComponent();
    }
}
