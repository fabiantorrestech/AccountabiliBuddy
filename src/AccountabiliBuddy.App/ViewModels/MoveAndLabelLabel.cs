using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A label as Move and label shows it: its full name ("Work - AI"), its name under its category ("AI") and its colour.</summary>
public sealed record MoveAndLabelLabel(Guid Id, string FullName, string ShortName, Color Color);
