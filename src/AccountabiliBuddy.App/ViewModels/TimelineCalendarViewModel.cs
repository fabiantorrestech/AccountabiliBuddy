using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One calendar in Settings → Day bar, with whether its events show on the timeline's Calendar row.</summary>
public sealed partial class TimelineCalendarViewModel : ObservableObject
{
    public TimelineCalendarViewModel(string id, string name, string sourceName, string color, bool onTimeline)
    {
        Id = id;
        Name = name;
        SourceName = sourceName;
        Color = color;
        ColorBrush = Avalonia.Media.Color.TryParse(color, out Avalonia.Media.Color parsed) ? new SolidColorBrush(parsed) : Brushes.Gray;
        OnTimeline = onTimeline;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Where it comes from, e.g. "Work account" or the in-app calendar.</summary>
    public string SourceName { get; }

    /// <summary>"#RRGGBB", as the calendar has it.</summary>
    public string Color { get; }

    public IBrush ColorBrush { get; }

    [ObservableProperty]
    public partial bool OnTimeline { get; set; }
}
