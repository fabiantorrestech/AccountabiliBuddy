using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

public partial class DayPlanView : UserControl
{
    public DayPlanView()
    {
        InitializeComponent();
    }

    /// <summary>Scrolls the selected day type's editor into view (a part was clicked on the Plan page).</summary>
    public void ShowTemplateEditor()
    {
        TemplateEditor.BringIntoView();
    }
}
