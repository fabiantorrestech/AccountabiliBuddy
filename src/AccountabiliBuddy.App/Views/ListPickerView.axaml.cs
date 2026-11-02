using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>"From your lists" (see <c>ListPickerViewModel</c>). Code-behind: the keyboard goes to the search box as the picker opens.</summary>
public partial class ListPickerView : UserControl
{
    public ListPickerView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Background);
    }
}
