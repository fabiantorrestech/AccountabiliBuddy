using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Is this on track?" prompt for an app no rule covers. The answer becomes a rule in the session's profile:
/// by default for the whole app (process name), or, when "only this view" is chosen, for window titles
/// containing the (editable) text.
/// </summary>
public sealed partial class ClassifyPromptViewModel : ObservableObject
{
    private const string RuleNote = "Added from the classify prompt";

    private readonly string _windowTitle;

    public ClassifyPromptViewModel(string profileName, string processName, string windowTitle)
    {
        ProfileName = profileName;
        ProcessName = processName;
        _windowTitle = windowTitle;
        Pattern = processName;
    }

    public string ProfileName { get; }

    public string ProcessName { get; }

    public string Question => $"Is {ProcessName} on track for {ProfileName}?";

    public string WindowTitleText => string.IsNullOrWhiteSpace(_windowTitle) ? "(no window title)" : _windowTitle;

    /// <summary>False = match only windows whose title contains <see cref="Pattern"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchOnlyThisView))]
    [NotifyPropertyChangedFor(nameof(PatternLabel))]
    public partial bool MatchWholeApp { get; set; } = true;

    public bool MatchOnlyThisView
    {
        get => !MatchWholeApp;
        set => MatchWholeApp = !value;
    }

    public string PatternLabel => MatchWholeApp ? "App (process name)" : "Window title contains";

    [ObservableProperty]
    public partial string Pattern { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Raised with the new rule when the user answers.</summary>
    public event EventHandler<Rule>? Answered;

    /// <summary>Raised when the user picks "Not now".</summary>
    public event EventHandler? Deferred;

    partial void OnMatchWholeAppChanged(bool value)
    {
        Pattern = value ? ProcessName : _windowTitle;
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

    private void Answer(RuleVerdict verdict)
    {
        string pattern = Pattern.Trim();
        if (pattern.Length == 0)
        {
            Error = "Enter what to match first.";
            return;
        }

        Rule rule = MatchWholeApp
            ? new Rule(Guid.NewGuid(), MatchTarget.ProcessName, MatchKind.Exact, pattern, verdict, RuleNote)
            : new Rule(Guid.NewGuid(), MatchTarget.WindowTitle, MatchKind.Contains, pattern, verdict, RuleNote);

        Answered?.Invoke(this, rule);
    }
}
