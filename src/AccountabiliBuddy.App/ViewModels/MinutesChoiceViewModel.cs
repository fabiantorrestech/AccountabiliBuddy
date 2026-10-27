using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The logic of a split "+5 min" / "Snooze 9 min" button (shown by MinutesSplitButton): a main part for the default
/// minutes, a menu of presets and a custom 1-180, and a live line ("Goes off at 4:05 PM", "Rings again at 7:42 AM") from
/// the trusted clock, refreshed every second, that previews the choice under the pointer or the minutes being typed.
/// </summary>
public abstract partial class MinutesChoiceViewModel : ObservableObject, IDisposable
{
    public const int MinCustomMinutes = 1;
    public const int MaxCustomMinutes = 180;

    private readonly Func<DateTime> _localNow;
    private readonly Action<int> _onChoose;
    private readonly string _linePrefix;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private int? _hoveredMinutes;

    /// <param name="defaultMinutes">What the main part of the button does.</param>
    /// <param name="presets">The menu entries, in minutes.</param>
    /// <param name="presetFormat">Text of a button or menu entry, e.g. "+{0} min".</param>
    /// <param name="linePrefix">Start of the time line, e.g. "Goes off at".</param>
    protected MinutesChoiceViewModel(
        int defaultMinutes, IReadOnlyList<int> presets, string presetFormat, string linePrefix, MaterialIconKind icon,
        Func<DateTime> localNow, Action<int> onChoose)
    {
        DefaultMinutes = defaultMinutes;
        Icon = icon;
        _linePrefix = linePrefix;
        _localNow = localNow;
        _onChoose = onChoose;
        DefaultText = string.Format(CultureInfo.InvariantCulture, presetFormat, defaultMinutes);
        Presets = presets.Select(minutes => new MinutesPreset(minutes, string.Format(CultureInfo.InvariantCulture, presetFormat, minutes))).ToList();
        _clock.Tick += (_, _) => RefreshTimeLine();
        _clock.Start();
        RefreshTimeLine();
    }

    public int DefaultMinutes { get; }

    public string DefaultText { get; }

    /// <summary>The text of the main part of the button.</summary>
    public virtual string MainText => DefaultText;

    public MaterialIconKind Icon { get; }

    public IReadOnlyList<MinutesPreset> Presets { get; }

    /// <summary>The time the shown choice (hovered, typed, or the default) would give.</summary>
    [ObservableProperty]
    private string _timeLine = "";

    /// <summary>The minutes typed in the custom box.</summary>
    [ObservableProperty]
    private string _customMinutesText = "";

    [ObservableProperty]
    private bool _isCustomInvalid;

    /// <summary>The choice under the pointer, or null to show the default again.</summary>
    public void Hover(int? minutes)
    {
        _hoveredMinutes = minutes;
        RefreshTimeLine();
    }

    partial void OnCustomMinutesTextChanged(string value)
    {
        IsCustomInvalid = value.Length > 0 && !TryReadCustom(out _);
        RefreshTimeLine();
    }

    /// <summary>The main part of the split button, and every menu entry (parameter = minutes).</summary>
    [RelayCommand]
    private void Choose(object? minutes)
    {
        int count = minutes is null ? DefaultMinutes : Convert.ToInt32(minutes, CultureInfo.InvariantCulture);
        _onChoose(count);
    }

    /// <summary>Enter in the custom box.</summary>
    [RelayCommand]
    private void ApplyCustom()
    {
        if (TryReadCustom(out int minutes))
        {
            _onChoose(minutes);
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

    private void RefreshTimeLine()
    {
        int minutes = _hoveredMinutes ?? (TryReadCustom(out int typed) ? typed : DefaultMinutes);
        DateTime at = _localNow().AddMinutes(minutes);
        TimeLine = $"{_linePrefix} {at.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    }
}
