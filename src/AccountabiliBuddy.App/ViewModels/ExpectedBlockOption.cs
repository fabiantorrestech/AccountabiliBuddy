using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A Cold Turkey block in the profile editor, ticked when the profile expects it on during its focus.</summary>
public sealed partial class ExpectedBlockOption : ObservableObject
{
    public ExpectedBlockOption(string name, bool isExpected)
    {
        Name = name;
        IsExpected = isExpected;
    }

    public string Name { get; }

    [ObservableProperty]
    public partial bool IsExpected { get; set; }
}
