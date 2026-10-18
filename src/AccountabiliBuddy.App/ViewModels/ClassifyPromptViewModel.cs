using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Is this on track?" prompt for activity no rule covers, shown during focus until it is answered (there is no
/// "not now"; unanswered activity escalates). Browser tabs default to the site (domain); other apps default to the
/// whole app. The user can narrow it to the window title, or to the channel on YouTube, and edit the text.
/// <list type="bullet">
/// <item>On track: allowed for this session only, and needs a reason (at least one word).</item>
/// <item>Add exemption (the quieter option): a permanent Allow rule in the session's profile and any others ticked,
/// right away; needs a reason too.</item>
/// <item>Distracting: a Block rule in the session's profile; the reason is optional.</item>
/// </list>
/// Reasons are logged with the answer (encrypted) so patterns can be spotted later.
/// </summary>
public sealed partial class ClassifyPromptViewModel : ObservableObject
{
    private const string RuleNote = "Added from the classify prompt";

    private readonly string _processName;
    private readonly string _windowTitle;
    private readonly string? _site;
    private readonly string? _channel;
    private readonly ActivitySnapshot _snapshot;

    /// <param name="otherProfiles">Profiles besides the session's that an exemption can also be added to.</param>
    public ClassifyPromptViewModel(string profileName, ActivitySnapshot snapshot, IReadOnlyList<ProfileOption> otherProfiles)
    {
        ProfileName = profileName;
        _snapshot = snapshot;
        OtherProfiles = otherProfiles.Select(profile => new ProfileChoiceViewModel(profile)).ToList();
        Reason = string.Empty;
        _processName = snapshot.ProcessName;
        _windowTitle = snapshot.WindowTitle;
        _site = snapshot.Url is null ? null : UrlNormalizer.GetHost(snapshot.Url);
        _channel = snapshot.Channel;

        Subject = EnforcementEngine.ClassificationSubject(snapshot);
        Scope = _site is null ? ClassifyScope.WholeApp : ClassifyScope.Site;
        Pattern = DefaultPattern(Scope);
    }

    public string ProfileName { get; }

    /// <summary>The site or app the question is about; also the key for "Not now".</summary>
    public string Subject { get; }

    public string Question => $"Is {Subject} on track for {ProfileName}?";

    public string WindowTitleText => string.IsNullOrWhiteSpace(_windowTitle) ? "(no window title)" : _windowTitle;

    public bool HasSite => _site is not null;

    public bool HasChannel => _channel is not null;

    public string WholeAppLabel => $"The whole app ({_processName})";

    public string SiteLabel => $"This site ({_site})";

    public string ChannelLabel => $"This channel ({_channel})";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWholeApp), nameof(IsSite), nameof(IsTitle), nameof(IsChannel), nameof(PatternLabel))]
    public partial ClassifyScope Scope { get; set; }

    public bool IsWholeApp
    {
        get => Scope == ClassifyScope.WholeApp;
        set => SelectScope(ClassifyScope.WholeApp, value);
    }

    public bool IsSite
    {
        get => Scope == ClassifyScope.Site;
        set => SelectScope(ClassifyScope.Site, value);
    }

    public bool IsTitle
    {
        get => Scope == ClassifyScope.Title;
        set => SelectScope(ClassifyScope.Title, value);
    }

    public bool IsChannel
    {
        get => Scope == ClassifyScope.Channel;
        set => SelectScope(ClassifyScope.Channel, value);
    }

    public string PatternLabel => Scope switch
    {
        ClassifyScope.Site => "Site (domain, includes subdomains)",
        ClassifyScope.Title => "Window title contains",
        ClassifyScope.Channel => "Channel name",
        _ => "App (process name)",
    };

    [ObservableProperty]
    public partial string Pattern { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Why; required to allow anything.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; }

    /// <summary>True while the "add exemption" panel is open.</summary>
    [ObservableProperty]
    public partial bool IsExemptionOpen { get; set; }

    public string ExemptionProfileText => $"Always allowed in {ProfileName}, and also in:";

    public IReadOnlyList<ProfileChoiceViewModel> OtherProfiles { get; }

    public bool HasOtherProfiles => OtherProfiles.Count > 0;

    /// <summary>Raised when the user answers.</summary>
    public event EventHandler<ClassifyAnswer>? Answered;

    partial void OnScopeChanged(ClassifyScope value)
    {
        Pattern = DefaultPattern(value);
        Error = null;
    }

    partial void OnReasonChanged(string value)
    {
        Error = null;
    }

    [RelayCommand]
    private void MarkOnTrack()
    {
        Answer(RuleVerdict.Allow, LabelLifetime.Session, Array.Empty<Guid>());
    }

    [RelayCommand]
    private void MarkDistracting()
    {
        Answer(RuleVerdict.Block, LabelLifetime.Permanent, Array.Empty<Guid>());
    }

    [RelayCommand]
    private void ToggleExemption()
    {
        IsExemptionOpen = !IsExemptionOpen;
    }

    [RelayCommand]
    private void AddExemption()
    {
        IReadOnlyList<Guid> others = OtherProfiles.Where(profile => profile.IsSelected).Select(profile => profile.Id).ToList();
        Answer(RuleVerdict.Allow, LabelLifetime.Permanent, others);
    }

    private void SelectScope(ClassifyScope scope, bool selected)
    {
        if (selected)
        {
            Scope = scope;
        }
    }

    private string DefaultPattern(ClassifyScope scope)
    {
        return scope switch
        {
            ClassifyScope.Site => _site ?? string.Empty,
            ClassifyScope.Title => _windowTitle,
            ClassifyScope.Channel => _channel ?? string.Empty,
            _ => _processName,
        };
    }

    private void Answer(RuleVerdict verdict, LabelLifetime lifetime, IReadOnlyList<Guid> otherProfileIds)
    {
        string pattern = Pattern.Trim();
        if (pattern.Length == 0)
        {
            Error = "Enter what to match first.";
            return;
        }

        string reason = Reason.Trim();
        if (verdict == RuleVerdict.Allow && !reason.Any(char.IsLetterOrDigit))
        {
            Error = "Write why first (one word is enough).";
            return;
        }

        (MatchTarget target, MatchKind kind) = Scope switch
        {
            ClassifyScope.Site => (MatchTarget.Url, MatchKind.Domain),
            ClassifyScope.Title => (MatchTarget.WindowTitle, MatchKind.Contains),
            ClassifyScope.Channel => (MatchTarget.Channel, MatchKind.Exact),
            _ => (MatchTarget.ProcessName, MatchKind.Exact),
        };

        var rule = new Rule(Guid.NewGuid(), target, kind, pattern, verdict, RuleNote);
        Answered?.Invoke(this, new ClassifyAnswer(rule, _snapshot, lifetime, reason.Length == 0 ? null : reason, otherProfileIds));
    }
}
