using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A profile as shown in pickers. <see cref="ToString"/> is what ComboBoxes display.</summary>
public sealed record ProfileOption(Guid Id, string Name)
{
    public static ProfileOption From(FocusProfile profile)
    {
        return new ProfileOption(profile.Id, profile.Name);
    }

    public override string ToString()
    {
        return Name;
    }
}
