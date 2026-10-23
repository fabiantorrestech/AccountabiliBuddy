using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The stacked prompt (redesign phase 2): when planning ends, every unknown site or app seen meanwhile as one row each, so
/// one window settles several. Per row: the scope (whole site or only certain parts) and Distracting, On track (this
/// session) or Exempt (always, also in the pre-ticked profiles); one reason covers every allow. A row left unanswered
/// stays unknown and is asked about as usual: there is no "not now".
/// </summary>
public sealed partial class ClassifyStackViewModel : ObservableObject
{
    private readonly IReadOnlySet<Guid> _preTicked;
    private readonly Guid _profileId;

    public ClassifyStackViewModel(
        string profileName, IReadOnlyList<ActivitySnapshot> unknowns, IReadOnlyList<ProfileOption> otherProfiles, IReadOnlySet<Guid>? preTicked,
        Guid profileId)
    {
        ProfileName = profileName;
        _profileId = profileId;
        _preTicked = preTicked ?? new HashSet<Guid>();
        Rows = unknowns.Select(snapshot => new ClassifyStackRowViewModel(new ClassifyPromptViewModel(profileName, snapshot, otherProfiles, preTicked))).ToList();
        Reason = string.Empty;
    }

    public string ProfileName { get; }

    public string Heading => $"What you opened while planning: on track for {ProfileName}?";

    public IReadOnlyList<ClassifyStackRowViewModel> Rows { get; }

    /// <summary>Why, for every On track and Exempt row.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Raised with the answers when Done succeeds.</summary>
    public event EventHandler<IReadOnlyList<ClassifyAnswer>>? Submitted;

    partial void OnReasonChanged(string value)
    {
        Error = null;
    }

    /// <summary>The answers for the answered rows, in row order; null with <see cref="Error"/> set when one can't be made.</summary>
    public IReadOnlyList<ClassifyAnswer>? Submit()
    {
        var answers = new List<ClassifyAnswer>();
        foreach (ClassifyStackRowViewModel row in Rows.Where(row => row.Verdict != StackVerdict.Unanswered))
        {
            (RuleVerdict verdict, LabelLifetime lifetime, IReadOnlyList<Guid> others) = row.Verdict switch
            {
                StackVerdict.Distracting => (RuleVerdict.Block, LabelLifetime.Permanent, (IReadOnlyList<Guid>)Array.Empty<Guid>()),
                StackVerdict.OnTrack => (RuleVerdict.Allow, LabelLifetime.Session, Array.Empty<Guid>()),
                _ => (RuleVerdict.Allow, LabelLifetime.Permanent, row.Prompt.OtherProfiles.Where(profile => profile.IsSelected).Select(profile => profile.Id).ToList()),
            };

            if (row.Prompt.BuildAnswer(verdict, lifetime, others, Reason, out string? error) is not ClassifyAnswer answer)
            {
                Error = verdict == RuleVerdict.Allow && error is not null && error.StartsWith("Write why", StringComparison.Ordinal)
                    ? "Write why for the On track and Exempt rows (one word is enough)."
                    : $"{row.Prompt.Subject}: {error}";
                return null;
            }

            // The profile it was asked for, whatever runs when it's answered.
            answers.Add(answer with { ProfileId = _profileId });
        }

        return answers;
    }

    [RelayCommand]
    private void Done()
    {
        if (Submit() is { } answers)
        {
            Submitted?.Invoke(this, answers);
        }
    }
}
