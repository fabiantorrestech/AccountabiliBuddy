using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One tickable choice in Settings → Play when headphones connect → "Only at certain times": a weekday or a plan category.</summary>
public sealed partial class HeadphoneOptionViewModel : ObservableObject
{
    private readonly Action _changed;

    public HeadphoneOptionViewModel(string label, object value, bool isChosen, Action changed)
    {
        Label = label;
        Value = value;
        _changed = changed;
        IsChosen = isChosen;
    }

    public string Label { get; }

    /// <summary>A <see cref="DayOfWeek"/> or a category <see cref="Guid"/>.</summary>
    public object Value { get; }

    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    partial void OnIsChosenChanged(bool value) => _changed();
}
