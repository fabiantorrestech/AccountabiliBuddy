using System.Collections.ObjectModel;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Shared helpers for keeping profile pickers in sync with saved profiles.</summary>
public static class ProfileOptions
{
    /// <summary>Replaces the options with <paramref name="profiles"/> and returns the option matching <paramref name="keepSelectedId"/>, if still present.</summary>
    public static ProfileOption? Refresh(
        ObservableCollection<ProfileOption> options,
        IEnumerable<FocusProfile> profiles,
        Guid? keepSelectedId)
    {
        options.Clear();
        foreach (FocusProfile profile in profiles)
        {
            options.Add(ProfileOption.From(profile));
        }

        return options.FirstOrDefault(option => option.Id == keepSelectedId) ?? options.FirstOrDefault();
    }

    public static string? NameOf(IEnumerable<FocusProfile> profiles, Guid? profileId)
    {
        return profiles.FirstOrDefault(profile => profile.Id == profileId)?.Name;
    }
}
