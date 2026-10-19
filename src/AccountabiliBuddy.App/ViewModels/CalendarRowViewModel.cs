using AccountabiliBuddy.App.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One calendar in the Exceptions tab's Calendars list: show/hide and colour.</summary>
public sealed partial class CalendarRowViewModel : ObservableObject
{
    private readonly CalendarService _calendars;
    private readonly string _calendarId;

    public CalendarRowViewModel(CalendarService calendars, CalendarSource source, CalendarInfo calendar)
    {
        _calendars = calendars;
        _calendarId = calendar.Id;
        Name = calendar.Name;
        SourceName = source.Name;
        Brush = new SolidColorBrush(Color.Parse(calendar.Color));
        _isVisible = calendar.IsVisible;
    }

    public string Name { get; }

    public string SourceName { get; }

    public IBrush Brush { get; }

    private bool _isVisible;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                _calendars.SetVisible(_calendarId, value);
            }
        }
    }

    [RelayCommand]
    private void NextColor()
    {
        _calendars.NextColor(_calendarId);
    }
}
