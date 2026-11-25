using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One entry of an add box's "@" or "#" list: a label (its dot), "Create label “x”", a tag with how many tasks have it, or a new tag.
/// </summary>
public sealed partial class AddBoxSuggestionViewModel : ObservableObject
{
    private readonly Action<AddBoxSuggestionViewModel> _pick;

    public AddBoxSuggestionViewModel(string name, string title, string detail, IBrush? dot, bool isTag, bool creates, Action<AddBoxSuggestionViewModel> pick)
    {
        Name = name;
        Title = title;
        Detail = detail;
        Dot = dot;
        IsTag = isTag;
        Creates = creates;
        _pick = pick;
    }

    /// <summary>The label's or tag's name, as it is kept.</summary>
    public string Name { get; }

    /// <summary>What the entry reads: the name, or "Create label “x”".</summary>
    public string Title { get; }

    /// <summary>The faint text at its right: "3 tasks", "new tag", "Enter".</summary>
    public string Detail { get; }

    /// <summary>"Shared" or "This device" (ADR 89) on the first entry of each group; null elsewhere.</summary>
    public string? GroupHead { get; init; }

    public bool HasGroupHead => GroupHead is not null;

    /// <summary>
    /// The group heading's icon: the label category's own (Tasks redesign 5.53), else Devices for Shared, Laptop for This device (ADR 32;
    /// the same meanings as "Keep on…").
    /// </summary>
    public Material.Icons.MaterialIconKind GroupIcon => CategoryIcon
        ?? (OnlyHere || GroupHead == "This device" ? Material.Icons.MaterialIconKind.Laptop : Material.Icons.MaterialIconKind.Devices);

    /// <summary>The label category's icon on the first label of each category; null for the other headings.</summary>
    public Material.Icons.MaterialIconKind? CategoryIcon { get; init; }

    /// <summary>A label listed under its category heading: drawn indented.</summary>
    public bool IsIndented { get; init; }

    /// <summary>Picking it makes the label or tag device-only (it is created that way).</summary>
    public bool OnlyHere { get; init; }

    public IBrush? Dot { get; }

    public bool HasDot => Dot is not null;

    public bool IsTag { get; }

    /// <summary>Picking it makes a new label first.</summary>
    public bool Creates { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Pick() => _pick(this);
}
