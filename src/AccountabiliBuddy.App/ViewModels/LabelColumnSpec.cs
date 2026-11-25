using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One label column of Group by Labels (Tasks redesign 4c): its key, label (null for No label), names, colour and category band.</summary>
public sealed record LabelColumnSpec(
    string Key, Guid? LabelId, string Name, string ShortName, IBrush? Dot, string BandKey, string BandName, MaterialIconKind BandIcon, int BandTotal);
