using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One setting whose two modes' values differ (ADR 69), in the compare view: AccountabiliBuddy's on the left, ProductaBuddy's on the
/// right, and which one both modes should keep (neither = leave it as it is).
/// </summary>
public sealed partial class ModeCompareRowViewModel : ObservableObject
{
    public ModeCompareRowViewModel(ModeSettingRow row)
    {
        Row = row;
    }

    public ModeSettingRow Row { get; }

    public string Section => Row.Setting.Section;

    public string Label => Row.Setting.Label;

    public string GuardingText => Row.GuardingText;

    public string CompanionText => Row.CompanionText;

    /// <summary>Both modes keep AccountabiliBuddy's value.</summary>
    [ObservableProperty]
    public partial bool UseGuarding { get; set; }

    /// <summary>Both modes keep ProductaBuddy's value.</summary>
    [ObservableProperty]
    public partial bool UseCompanion { get; set; }

    public bool IsPicked => UseGuarding || UseCompanion;
}
