using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Is this on track?" prompt for activity no rule covers. The answer becomes a rule in the session's profile.
/// Browser tabs default to the site (domain); other apps default to the whole app. The user can narrow it to
/// the window title, or to the channel on YouTube, and edit the text before answering.
/// </summary>
public sealed partial class ClassifyPromptViewModel : ObservableObject
{
    private const string RuleNote = "Added from the classify prompt";

    private readonly string _processName;
    private readonly string _windowTitle;
    private readonly string? _site;
    private readonly string? _channel;

    public ClassifyPromptViewModel(string profileName, ActivitySnapshot snapshot)
    {
        ProfileName = profileName;
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

    /// <summary>Raised with the new rule when the user answers.</summary>
    public event EventHandler<Rule>? Answered;

    /// <summary>Raised when the user picks "Not now".</summary>
    public event EventHandler? Deferred;

    partial void OnScopeChanged(ClassifyScope value)
    {
        Pattern = DefaultPattern(value);
        Error = null;
    }

    [RelayCommand]
    private void MarkOnTrack()
    {
        Answer(RuleVerdict.Allow);
    }

    [RelayCommand]
    private void MarkDistracting()
    {
        Answer(RuleVerdict.Block);
    }

    [RelayCommand]
    private void NotNow()
    {
        Deferred?.Invoke(this, EventArgs.Empty);
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

    private void Answer(RuleVerdict verdict)
    {
        string pattern = Pattern.Trim();
        if (pattern.Length == 0)
        {
            Error = "Enter what to match first.";
            return;
        }

        (MatchTarget target, MatchKind kind) = Scope switch
        {
            ClassifyScope.Site => (MatchTarget.Url, MatchKind.Domain),
            ClassifyScope.Title => (MatchTarget.WindowTitle, MatchKind.Contains),
            ClassifyScope.Channel => (MatchTarget.Channel, MatchKind.Exact),
            _ => (MatchTarget.ProcessName, MatchKind.Exact),
        };

        Answered?.Invoke(this, new Rule(Guid.NewGuid(), target, kind, pattern, verdict, RuleNote));
    }
}
