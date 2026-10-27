using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "Timer · Tea is up" window: Dismiss, and a split "+5 min" button (+1, +5, +10, +15 or a custom 1-180) that also says
/// when the timer will go off again. That time is worked out from the trusted clock and refreshed every second, and
/// shows the choice under the pointer (or the custom minutes being typed) while there is one.
/// </summary>
public sealed partial class TimerUpViewModel : ObservableObject, IDisposable
{
    public const int DefaultMinutes = 5;
    public const int MinCustomMinutes = 1;
    public const int MaxCustomMinutes = 180;

    private readonly Func<DateTime> _localNow;
    private readonly Action _onDismiss;
    private readonly Action<int> _onExtend;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private int? _hoveredMinutes;

    public TimerUpViewModel(string title, string text, Func<DateTime> localNow, Action onDismiss, Action<int> onExtend)
    {
        Title = title;
        Text = text;
        _localNow = localNow;
        _onDismiss = onDismiss;
        _onExtend = onExtend;
        _clock.Tick += (_, _) => RefreshGoesOffAt();
        _clock.Start();
        RefreshGoesOffAt();
    }

    public string Title { get; }

    public string Text { get; }

    public string DefaultText => $"+{DefaultMinutes} min";

    /// <summary>"Goes off at 4:05 PM": the time the shown choice (hovered, typed, or the default) would give.</summary>
    [ObservableProperty]
    private string _goesOffAt = "";

    /// <summary>The minutes typed in the custom box.</summary>
    [ObservableProperty]
    private string _customMinutesText = "";

    [ObservableProperty]
    private bool _isCustomInvalid;

    /// <summary>The choice under the pointer, or null to show the default again.</summary>
    public void Hover(int? minutes)
    {
        _hoveredMinutes = minutes;
        RefreshGoesOffAt();
    }

    partial void OnCustomMinutesTextChanged(string value)
    {
        IsCustomInvalid = value.Length > 0 && !TryReadCustom(out _);
        RefreshGoesOffAt();
    }

    [RelayCommand]
    private void Dismiss() => _onDismiss();

    /// <summary>The main part of the split button, and every menu entry (parameter = minutes).</summary>
    [RelayCommand]
    private void Extend(object? minutes)
    {
        int count = minutes is null ? DefaultMinutes : Convert.ToInt32(minutes, CultureInfo.InvariantCulture);
        _onExtend(count);
    }

    /// <summary>Enter in the custom box.</summary>
    [RelayCommand]
    private void ApplyCustom()
    {
        if (TryReadCustom(out int minutes))
        {
            _onExtend(minutes);
        }
        else
        {
            IsCustomInvalid = true;
        }
    }

    public void Dispose() => _clock.Stop();

    private bool TryReadCustom(out int minutes) =>
        int.TryParse(CustomMinutesText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
        && minutes is >= MinCustomMinutes and <= MaxCustomMinutes;

    private void RefreshGoesOffAt()
    {
        int minutes = _hoveredMinutes ?? (TryReadCustom(out int typed) ? typed : DefaultMinutes);
        DateTime at = _localNow().AddMinutes(minutes);
        GoesOffAt = $"Goes off at {at.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    }
}
