using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Labels screen. Code-behind: clicking away from a label's name keeps it, like Enter; the header's + gives the new label row's
/// name box the keyboard.
/// </summary>
public partial class LabelsView : UserControl
{
    private LabelsViewModel? _shown;

    public LabelsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as LabelsViewModel);
    }

    private void Watch(LabelsViewModel? labels)
    {
        if (_shown is not null)
        {
            _shown.FocusNewRequested -= OnFocusNewRequested;
        }

        _shown = labels;
        if (labels is not null)
        {
            labels.FocusNewRequested += OnFocusNewRequested;
        }
    }

    private void OnFocusNewRequested(object? sender, EventArgs e)
    {
        NewNameBox.Focus();
    }

    private void OnNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: LabelRowViewModel row })
        {
            row.SaveNameCommand.Execute(null);
        }
    }
}
