using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A profile with a checkbox, e.g. "also add this exemption to …" in the classify prompt.</summary>
public sealed partial class ProfileChoiceViewModel : ObservableObject
{
    public ProfileChoiceViewModel(ProfileOption profile)
    {
        Id = profile.Id;
        Name = profile.Name;
    }

    public Guid Id { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
