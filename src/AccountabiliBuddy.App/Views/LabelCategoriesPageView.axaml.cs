using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>The setup assistant's "Label categories" page (ADR 93); its data is <see cref="ViewModels.SetupAssistantViewModel"/>'s category rows.</summary>
public partial class LabelCategoriesPageView : UserControl
{
    public LabelCategoriesPageView()
    {
        InitializeComponent();
    }
}
