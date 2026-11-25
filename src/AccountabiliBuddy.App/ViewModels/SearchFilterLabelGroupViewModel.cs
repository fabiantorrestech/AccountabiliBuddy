using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A category of Search filters' Labels (Tasks redesign 7.19): its icon and name over its label chips; "Other labels" comes last.</summary>
public sealed class SearchFilterLabelGroupViewModel
{
    public SearchFilterLabelGroupViewModel(string name, MaterialIconKind icon, IReadOnlyList<SearchFilterPickChipViewModel> chips)
    {
        Name = name;
        Icon = icon;
        Chips = chips;
    }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    public IReadOnlyList<SearchFilterPickChipViewModel> Chips { get; }
}
