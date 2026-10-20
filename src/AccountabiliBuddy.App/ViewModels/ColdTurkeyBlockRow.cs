using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One Cold Turkey block in the Session tab: a coloured dot, its name, and whether it's on.</summary>
public sealed record ColdTurkeyBlockRow(string Name, string StateText, IBrush Dot);
