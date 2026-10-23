using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

public partial class WorkGateWindow : Window
{
    public WorkGateWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Dispatcher.UIThread.Post(() => Line.Focus());
    }
}
