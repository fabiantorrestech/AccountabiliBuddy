using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

public partial class ProfilesView : UserControl
{
    // Below this width the list sits above the chosen profile instead of beside it.
    private const double SideBySideWidth = 640;

    public ProfilesView()
    {
        InitializeComponent();
        Layout.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    private void Arrange(double width)
    {
        bool stacked = width < SideBySideWidth;
        Layout.ColumnDefinitions[0].Width = stacked ? GridLength.Star : new GridLength(250);
        Layout.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Star;
        Grid.SetRow(DetailColumn, stacked ? 1 : 0);
        Grid.SetColumn(DetailColumn, stacked ? 0 : 1);
        Grid.SetColumnSpan(DetailColumn, stacked ? 2 : 1);
        Layout.RowSpacing = stacked ? 12 : 0;
    }
}
