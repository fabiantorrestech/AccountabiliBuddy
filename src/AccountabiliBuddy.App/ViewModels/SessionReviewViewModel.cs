using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The review card (redesign phase 2): after a session, the sites and apps it used with no rule, each Distracting (a block,
/// at once), Fine (an allow into the session's profile and the pre-ticked ones; needs the reason, as an exemption does) or
/// Ignore (never proposed again). The same card lists the week's in History ("New this week"), where the profile is picked.
/// </summary>
public sealed partial class SessionReviewViewModel : ObservableObject
{
    private readonly Func<SessionReviewViewModel, Task> _apply;

    /// <param name="profiles">The profiles a week's rows can be written to; null for one session (its own profile).</param>
    /// <param name="apply">Writes the choices (set by the service).</param>
    public SessionReviewViewModel(
        string heading, IReadOnlyList<ReviewRowViewModel> rows, Guid profileId, IReadOnlyList<ProfileOption>? profiles, Func<SessionReviewViewModel, Task> apply)
    {
        Heading = heading;
        Rows = rows;
        _apply = apply;
        ProfileChoices = profiles ?? [];
        SelectedProfile = ProfileChoices.FirstOrDefault(profile => profile.Id == profileId) ?? ProfileChoices.FirstOrDefault();
        ProfileId = profileId;
        Reason = string.Empty;
    }

    /// <summary>E.g. "Last session: what had no rule".</summary>
    public string Heading { get; }

    public IReadOnlyList<ReviewRowViewModel> Rows { get; }

    public IReadOnlyList<ProfileOption> ProfileChoices { get; }

    /// <summary>The week's card: the rules go to the profile picked here.</summary>
    public bool HasProfileChoice => ProfileChoices.Count > 0;

    [ObservableProperty]
    public partial ProfileOption? SelectedProfile { get; set; }

    /// <summary>The profile the rules go to: the session's, or the one picked.</summary>
    public Guid ProfileId { get; private set; }

    partial void OnSelectedProfileChanged(ProfileOption? value)
    {
        if (value is not null)
        {
            ProfileId = value.Id;
        }
    }

    /// <summary>Why, for every Fine row.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    partial void OnReasonChanged(string value)
    {
        Error = null;
    }

    /// <summary>Writes the choices; false (with <see cref="Error"/>) when a Fine row has no reason.</summary>
    public async Task<bool> ApplyAsync()
    {
        if (Rows.Any(row => row.Choice == ReviewChoice.Fine) && !Reason.Any(char.IsLetterOrDigit))
        {
            Error = "Write why for the Fine rows (one word is enough).";
            return false;
        }

        await _apply(this);
        return true;
    }

    /// <summary>Raised when the card is done with (Done applied, or Skip): whoever shows it hides it.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private async Task DoneAsync()
    {
        if (await ApplyAsync())
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Nothing written; the rows may come up again.</summary>
    [RelayCommand]
    private void Skip()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
