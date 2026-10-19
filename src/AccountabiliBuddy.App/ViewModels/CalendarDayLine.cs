using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line inside a day cell of the Exceptions month view, e.g. "Off 12:00–14:00" or "09:00–12:00 Coding".</summary>
public sealed record CalendarDayLine(string Text, IBrush Brush);
