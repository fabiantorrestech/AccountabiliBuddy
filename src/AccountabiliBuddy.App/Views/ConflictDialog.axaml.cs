using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Views;

/// <summary>"Changed on both devices" (P9): opened from a row's sync-problem cloud or the open task's sync line.</summary>
public partial class ConflictDialog : TaskDialogWindow
{
    /// <summary>The newer version's text is bold.</summary>
    public static readonly IValueConverter WeightOf = new FuncValueConverter<bool, FontWeight>(newer => newer ? FontWeight.SemiBold : FontWeight.Normal);

    /// <summary>"(no note)" is italic.</summary>
    public static readonly IValueConverter StyleOf = new FuncValueConverter<bool, FontStyle>(empty => empty ? FontStyle.Italic : FontStyle.Normal);

    public ConflictDialog()
    {
        InitializeComponent();
    }

    public ConflictDialog(ConflictDialogViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Closed += (_, _) => viewModel.Detach();
    }

    /// <summary>Shows the dialog over <paramref name="opener"/>'s window; the keyboard goes back to it afterwards.</summary>
    public static Task ShowAsync(Visual? opener, ConflictDialogViewModel viewModel)
    {
        return new ConflictDialog(viewModel).ShowOverAsync(opener);
    }
}
