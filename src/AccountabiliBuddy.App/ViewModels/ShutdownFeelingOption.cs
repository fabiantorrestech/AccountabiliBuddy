using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One of the five "how did today go" choices, 1 (rough) to 5 (great).</summary>
public sealed partial class ShutdownFeelingOption(int value, string label) : ObservableObject
{
    public int Value { get; } = value;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
