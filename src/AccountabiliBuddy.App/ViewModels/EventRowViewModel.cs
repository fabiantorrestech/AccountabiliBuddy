using AccountabiliBuddy.App.Services;
using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One event of the selected day in the Exceptions tab.</summary>
public sealed record EventRowViewModel(CalendarEntry Entry)
{
    public string Title => Entry.Event.Title;

    public string When => Entry.Event.IsAllDay
        ? "All day"
        : $"{Entry.Event.LocalStart:HH\\:mm}–{Entry.Event.LocalEnd:HH\\:mm}";

    public string CalendarName => $"{Entry.Calendar.Name} · {Entry.Source.Name}";

    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(Entry.Calendar.Color));

    public bool IsEditable => Entry.IsEditable;
}
