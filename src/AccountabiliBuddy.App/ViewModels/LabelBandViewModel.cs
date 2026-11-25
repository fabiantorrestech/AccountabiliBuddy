using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One band of Group by Labels' band row: a category's icon and name over its columns on this page, and "4 of 7 on this page" when the
/// rest of its labels are on other pages.
/// </summary>
public sealed partial class LabelBandViewModel : ObservableObject
{
    public LabelBandViewModel(string key, string name, MaterialIconKind icon, int total)
    {
        Key = key;
        Name = name;
        Icon = icon;
        Total = total;
        Note = string.Empty;
    }

    public string Key { get; }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    /// <summary>How many labels the category has in all.</summary>
    public int Total { get; }

    /// <summary>How many columns of this page it spans.</summary>
    public int Span { get; set; }

    /// <summary>"4 of 7 on this page"; empty when all of it is on the page.</summary>
    public string Note { get; private set; }

    public bool HasNote => Note.Length > 0;

    /// <summary>The note once the span is known.</summary>
    public void Finish()
    {
        Note = Span < Total ? $"{Span} of {Total} on this page" : string.Empty;
    }
}
