using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>Settings as a list and the chosen row's page (design 5q); the rows' pages are existing views.</summary>
public partial class SettingsShellView : UserControl
{
    // Under this width (the 900-wide minimum window) the list column narrows from 280 to 220 so the page keeps its room.
    private const double NarrowWidth = 840;

    public SettingsShellView()
    {
        InitializeComponent();
        Layout.SizeChanged += (_, e) => Layout.ColumnDefinitions[0].Width = new GridLength(e.NewSize.Width < NarrowWidth ? 220 : 280);
    }
}
