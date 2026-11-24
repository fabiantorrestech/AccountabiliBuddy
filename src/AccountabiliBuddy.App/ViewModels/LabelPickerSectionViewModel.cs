using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One category in the label pane: its heading (icon and name) and the labels shown under it on the guide line.</summary>
public sealed record LabelPickerSectionViewModel(string Name, MaterialIconKind Icon, IReadOnlyList<LabelPickerItemViewModel> Items);
