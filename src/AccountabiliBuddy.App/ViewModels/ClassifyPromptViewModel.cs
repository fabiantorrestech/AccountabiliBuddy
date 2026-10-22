using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Is this on track?" prompt for activity no rule covers, shown during focus until it is answered (there is no
/// "not now"; unanswered activity escalates). For a browser tab it asks first: the whole site, or only certain parts
/// (the page's section, channel or path; only this page; or only this view by its window title), so a part answers
/// for that part and the rest of the site is still asked about. Other apps: the whole app or only this view. The
/// pattern can be edited before answering.
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
    private readonly SitePart? _part;
    private readonly SitePart? _page;
    private readonly ActivitySnapshot _snapshot;

    /// <param name="otherProfiles">Profiles besides the session's that an exemption can also be added to.</param>
    /// <param name="preTicked">Of those, the ones ticked from the start (Profiles tab).</param>
    public ClassifyPromptViewModel(
        string profileName, ActivitySnapshot snapshot, IReadOnlyList<ProfileOption> otherProfiles, IReadOnlySet<Guid>? preTicked = null)
    {
        ProfileName = profileName;
        _snapshot = snapshot;
        OtherProfiles = otherProfiles.Select(profile => new ProfileChoiceViewModel(profile, preTicked?.Contains(profile.Id) == true)).ToList();
        Reason = string.Empty;
        _processName = snapshot.ProcessName;
        _windowTitle = snapshot.WindowTitle;
        _site = snapshot.Url is null ? null : UrlNormalizer.GetHost(snapshot.Url);
        _part = snapshot.Url is null ? null : SiteParts.PartOf(snapshot.Url, snapshot.Channel);
        _page = snapshot.Url is null ? null : SiteParts.PageOf(snapshot.Url);

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

    public bool HasPart => _part is not null;

    public bool HasPage => _page is not null;

    public string WholeAppLabel => HasSite ? $"The whole browser ({_processName})" : $"The whole app ({_processName})";

    public string SiteLabel => $"The whole site ({_site})";

    public string PartLabel => _part?.Label ?? string.Empty;

    public string PageLabel => _page?.Label ?? string.Empty;

    /// <summary>E.g. "Only this part: the rest of reddit.com is still asked about."</summary>
    public string PartsNote => $"Only this part: the rest of {_site} is still asked about.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWholeApp), nameof(IsSite), nameof(IsTitle), nameof(IsPart), nameof(IsPage), nameof(IsOnlyParts), nameof(PatternLabel))]
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

    public bool IsPart
    {
        get => Scope == ClassifyScope.Part;
        set => SelectScope(ClassifyScope.Part, value);
    }

    public bool IsPage
    {
        get => Scope == ClassifyScope.Page;
        set => SelectScope(ClassifyScope.Page, value);
    }

    /// <summary>"Only certain parts" (a browser tab): its section or path, this page, or this view.</summary>
    public bool IsOnlyParts
    {
        get => Scope is ClassifyScope.Part or ClassifyScope.Page or ClassifyScope.Title;
        set
        {
            if (value && !IsOnlyParts)
            {
                Scope = HasPart ? ClassifyScope.Part : HasPage ? ClassifyScope.Page : ClassifyScope.Title;
            }
        }
    }

    public string PatternLabel => Scope switch
    {
        ClassifyScope.Site => "Site (domain, includes subdomains)",
        ClassifyScope.Title => "Window title contains",
        ClassifyScope.Part when _part?.Target == MatchTarget.Channel => "Channel name",
        ClassifyScope.Part => "Address starts with",
        ClassifyScope.Page => _page?.Kind == MatchKind.Exact ? "Address (this page)" : "Address starts with (this page, any query)",
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
            ClassifyScope.Part => _part?.Pattern ?? string.Empty,
            ClassifyScope.Page => _page?.Pattern ?? string.Empty,
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
            ClassifyScope.Part when _part is not null => (_part.Target, _part.Kind),
            ClassifyScope.Page when _page is not null => (_page.Target, _page.Kind),
            _ => (MatchTarget.ProcessName, MatchKind.Exact),
        };

        var rule = new Rule(Guid.NewGuid(), target, kind, pattern, verdict, RuleNote);
        Answered?.Invoke(this, new ClassifyAnswer(rule, _snapshot, lifetime, reason.Length == 0 ? null : reason, otherProfileIds));
    }
}
