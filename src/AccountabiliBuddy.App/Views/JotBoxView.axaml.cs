using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Jot it down on the Today page and in the Today column (<see cref="AddBoxViewModel"/> with <see cref="AddBoxKind.Jot"/>). Both places
/// share one jot; the one on screen takes the keyboard when it opens. Code-behind: the keyboard and focus (<see cref="AddBoxInput"/>).
/// </summary>
public partial class JotBoxView : UserControl
{
    private readonly AddBoxInput _input;

    public JotBoxView()
    {
        InitializeComponent();
        _input = new AddBoxInput(Box);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _input.Attach(DataContext as AddBoxViewModel);
    }
}
