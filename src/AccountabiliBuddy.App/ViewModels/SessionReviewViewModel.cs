using AccountabiliBuddy.App.Services;
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
        string heading, IReadOnlyList<ReviewRowViewModel> rows, Guid profileId, IReadOnlyList<ProfileOption>? profiles, Func<SessionReviewViewModel, Task> apply,
        IReadOnlyList<PromoteRowViewModel>? promotions = null, IReadOnlyList<string>? notes = null)
    {
        Heading = heading;
        Notes = notes ?? [];
        Rows = rows;
        Promotions = promotions ?? [];
        _apply = apply;
        ProfileChoices = profiles ?? [];
        SelectedProfile = ProfileChoices.FirstOrDefault(profile => profile.Id == profileId) ?? ProfileChoices.FirstOrDefault();
        ProfileId = profileId;
        Reason = string.Empty;
    }

    /// <summary>When the reviewed session started (null for the week's card).</summary>
    public DateTimeOffset? SessionStart { get; init; }

    /// <summary>Tasks added past the 3-task limit during the session.</summary>
    public int OverLimitCount { get; init; }

    public bool HasOverLimit => OverLimitCount > 0;

    /// <summary>E.g. "Over 3 tasks: 2".</summary>
    public string OverLimitText => $"Over {SessionTasksService.MaxTasks} tasks: {OverLimitCount}";

    /// <summary>"Phone: picked up 2× · longest down 18m" (ADR 26); empty until the phone's file comes.</summary>
    [ObservableProperty]
    public partial string PhoneText { get; set; } = string.Empty;

    /// <summary>E.g. "Last session: what had no rule".</summary>
    public string Heading { get; }

    public IReadOnlyList<ReviewRowViewModel> Rows { get; }

    /// <summary>Sites and apps called on track in three sessions, offered once as permanent exemptions.</summary>
    public IReadOnlyList<PromoteRowViewModel> Promotions { get; }

    public bool HasRows => Rows.Count > 0;

    public bool HasPromotions => Promotions.Count > 0;

    /// <summary>Notes kept during the session (redesign phase 5), shown once here.</summary>
    public IReadOnlyList<string> Notes { get; }

    public bool HasNotes => Notes.Count > 0;

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
        if ((Rows.Any(row => row.Choice == ReviewChoice.Fine) || Promotions.Any(row => row.IsYes)) && !Reason.Any(char.IsLetterOrDigit))
        {
            Error = "Write why for what you allow (one word is enough).";
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
    /// <summary>Raised on Skip (the service still marks the offers as made: they're offered once).</summary>
    public event EventHandler? Skipped;

    [RelayCommand]
    private void Skip()
    {
        Skipped?.Invoke(this, EventArgs.Empty);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
