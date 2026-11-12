using System.Windows.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One icon a view can have (its Material name, e.g. "lightning-bolt"); the picked one is ringed.</summary>
public sealed record ViewIconChoiceViewModel(string Name, MaterialIconKind Kind, bool IsSelected, ICommand Pick);
