using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One of the colour editor's red, green and blue rows: a slider and a box on the same 0 to 255 value.</summary>
public sealed partial class ColorChannelViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _setting;

    public ColorChannelViewModel(string label, Action changed)
    {
        Label = label;
        _changed = changed;
    }

    public string Label { get; }

    /// <summary>The slider's value, 0 to 255.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoxValue))]
    public partial double Value { get; set; }

    /// <summary>The number box's value; emptying the box leaves the value as it was.</summary>
    public decimal? BoxValue
    {
        get => (decimal)Math.Round(Value);
        set
        {
            if (value is decimal typed)
            {
                Value = (double)Math.Clamp(Math.Round(typed), 0, 255);
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The whole number this channel holds.</summary>
    public byte Whole => (byte)Math.Clamp(Math.Round(Value), 0, 255);

    /// <summary>Set from the hex box or a preset: no change is reported back to the editor.</summary>
    public void SetQuietly(byte value)
    {
        _setting = true;
        Value = value;
        _setting = false;
    }

    partial void OnValueChanged(double value)
    {
        if (!_setting)
        {
            _changed();
        }
    }
}
