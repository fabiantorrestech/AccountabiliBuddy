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

    public CalendarRowViewModel(CalendarService calendars, CalendarSource source, CalendarInfo calendar, CalendarRuleViewModel rule)
    {
        _calendars = calendars;
        _calendarId = calendar.Id;
        Rule = rule;
        Name = calendar.Name;
        SourceName = source.Name;
        _brush = new SolidColorBrush(Color.Parse(calendar.Color));
        _color = calendar.Color;
        _isVisible = calendar.IsVisible;
    }

    public string Name { get; }

    public string CalendarId => _calendarId;

    /// <summary>The rule choice under the row (ADR 58): Off, Keywords or Every event → category.</summary>
    public CalendarRuleViewModel Rule { get; }

    public string SourceName { get; }

    private IBrush _brush;
    private string _color;

    public IBrush Brush => _brush;

    /// <summary>Takes the calendar's colour and visibility as they are now, without writing them back.</summary>
    public void Update(CalendarInfo calendar)
    {
        if (calendar.Color != _color)
        {
            _color = calendar.Color;
            _brush = new SolidColorBrush(Color.Parse(calendar.Color));
            OnPropertyChanged(nameof(Brush));
        }

        if (calendar.IsVisible != _isVisible)
        {
            _isVisible = calendar.IsVisible;
            OnPropertyChanged(nameof(IsVisible));
        }
    }

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
