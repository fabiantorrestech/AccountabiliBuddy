using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

public partial class ExceptionsView : UserControl
{
    public ExceptionsView()
    {
        InitializeComponent();
    }

    /// <summary>Scrolls the one-off day editor into view (a part was clicked on the Plan page).</summary>
    public void ShowOneOffEditor()
    {
        OneOffEditorHost.BringIntoView();
    }
}
