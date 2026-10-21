using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One part of a day's plan in the Exceptions tab's agenda: its name and times, in its category's colour.</summary>
/// <param name="Brush">The category's colour, for the stripe.</param>
/// <param name="Tint">The same colour, faint, for the row behind the text.</param>
public sealed record AgendaRow(string Name, string Times, IBrush Brush, IBrush Tint);
