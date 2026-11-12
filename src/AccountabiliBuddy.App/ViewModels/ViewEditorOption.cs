using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One tick of the Create / Edit view dialog's "Show only" lists (a label, a tag, a due-date or time-limit choice).</summary>
/// <param name="Value">What the view stores for it (a label id, a tag, "overdue", "le25", "2w").</param>
/// <param name="Dot">A label's colour dot; null for none.</param>
public sealed partial class ViewEditorOption(string value, string text, bool isChecked, IBrush? dot = null) : ObservableObject
{
    public string Value { get; } = value;

    public string Text { get; } = text;

    public IBrush? Dot { get; } = dot;

    public bool HasDot => Dot is not null;

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;
}
