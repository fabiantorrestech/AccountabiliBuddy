using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Editable copy of one <see cref="FocusProfile"/>. Changes apply only when the Profiles tab is saved.</summary>
public sealed partial class ProfileEditorViewModel : ObservableObject
{
    private const int DefaultGraceSeconds = 30;

    private ProfileEditorViewModel(FocusProfile profile, ProfileOverlay overlay)
    {
        Rules.CollectionChanged += OnRulesChanged;
        Id = profile.Id;
        Name = profile.Name;
        GraceSeconds = ((int)profile.UnknownGracePeriod.TotalSeconds).ToString();
        AskAboutUnlistedSites = profile.AskAboutUnlistedSites;
        TimerColors = TimerColorOption.For(profile.Id);
        ShowOverlay(overlay);

        foreach (Rule rule in profile.Rules)
        {
            Rules.Add(new RuleEditorViewModel(rule, RemoveRule));
        }

        foreach (UnlockOption option in profile.UnlockOptions)
        {
            UnlockOptions.Add(new UnlockOptionEditorViewModel(option, RemoveUnlockOption));
        }
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>How long unknown activity is tolerated before asking "is this distracting?".</summary>
    [ObservableProperty]
    public partial string GraceSeconds { get; set; }

    /// <summary>A website no rule names counts as unknown even when the browser app is allowed.</summary>
    [ObservableProperty]
    public partial bool AskAboutUnlistedSites { get; set; }

    /// <summary>
    /// Ticked from the start when the classify prompt (another profile's session) adds an exemption, so it goes here
    /// too (this computer only).
    /// </summary>
    [ObservableProperty]
    public partial bool PreTickForExemptions { get; set; }

    /// <summary>
    /// The on-screen session timer during this profile's sessions (this computer only): a <see cref="TimerChoice"/>
    /// index (use the default, on or off).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnTimer))]
    public partial int TimerChoiceIndex { get; set; }

    /// <summary>True when the timer is on by the profile's own choice, so it has its own colour.</summary>
    public bool HasOwnTimer => TimerChoiceIndex == TimerChoice.On;

    [ObservableProperty]
    public partial TimerColorOption TimerColor { get; set; }

    public IReadOnlyList<TimerColorOption> TimerColors { get; }

    public ObservableCollection<RuleEditorViewModel> Rules { get; } = new();

    /// <summary>The ticked rules, to copy or move to another profile.</summary>
    public IReadOnlyList<RuleEditorViewModel> SelectedRules => Rules.Where(rule => rule.IsSelected).ToList();

    public bool HasSelectedRules => Rules.Any(rule => rule.IsSelected);

    /// <summary>E.g. "3 selected".</summary>
    public string SelectionText => $"{Rules.Count(rule => rule.IsSelected)} selected";

    /// <summary>"Select all": ticked when every rule is; ticking or unticking it does all of them.</summary>
    public bool AllRulesSelected
    {
        get => Rules.Count > 0 && Rules.All(rule => rule.IsSelected);
        set
        {
            foreach (RuleEditorViewModel rule in Rules)
            {
                rule.IsSelected = value;
            }
        }
    }

    /// <summary>Ways to lift this profile's blocks after a wait (Session tab → Unlock).</summary>
    public ObservableCollection<UnlockOptionEditorViewModel> UnlockOptions { get; } = new();

    /// <summary>Cold Turkey's blocks, ticked when this profile expects them on during its focus (this computer only).</summary>
    public ObservableCollection<ExpectedBlockOption> ColdTurkeyBlocks { get; } = new();

    public bool HasColdTurkeyBlocks => ColdTurkeyBlocks.Count > 0;

    public static ProfileEditorViewModel FromProfile(FocusProfile profile, ProfileOverlay? overlay = null)
    {
        return new ProfileEditorViewModel(profile, overlay ?? ProfileOverlay.FollowDefault);
    }

    public static ProfileEditorViewModel CreateNew()
    {
        var profile = new FocusProfile(
            Guid.NewGuid(),
            "New profile",
            Array.Empty<Rule>(),
            TimeSpan.FromSeconds(DefaultGraceSeconds))
        {
            UnlockOptions = new[] { UnlockOption.CreateOffDay() },
        };
        return new ProfileEditorViewModel(profile, ProfileOverlay.FollowDefault);
    }

    /// <summary>The on-screen timer choices, saved apart from the profile (they aren't part of the shared schema).</summary>
    public ProfileOverlay ToOverlay()
    {
        return TimerChoice.ToOverlay(TimerChoiceIndex, TimerColor.Hex);
    }

    /// <summary>
    /// Shows Cold Turkey's blocks with the expected ones ticked. An expected block Cold Turkey no longer has stays
    /// listed, so unticking it is possible.
    /// </summary>
    public void ShowColdTurkeyBlocks(IEnumerable<string> available, IReadOnlyCollection<string> expected)
    {
        ColdTurkeyBlocks.Clear();
        foreach (string name in available.Concat(expected).Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCultureIgnoreCase))
        {
            ColdTurkeyBlocks.Add(new ExpectedBlockOption(name, expected.Contains(name)));
        }

        OnPropertyChanged(nameof(HasColdTurkeyBlocks));
    }

    /// <summary>The ticked Cold Turkey blocks, saved apart from the profile (they aren't part of the shared schema).</summary>
    public IReadOnlyList<string> ToExpectedBlocks()
    {
        return ColdTurkeyBlocks.Where(option => option.IsExpected).Select(option => option.Name).ToList();
    }

    /// <summary>Shows saved timer choices, e.g. after they were changed in the Settings tab.</summary>
    [MemberNotNull(nameof(TimerColor))]
    public void ShowOverlay(ProfileOverlay overlay)
    {
        TimerChoiceIndex = TimerChoice.IndexOf(overlay);
        TimerColor = TimerColorOption.Match(TimerColors, overlay.Color);
    }

    [RelayCommand]
    private void AddRule()
    {
        var rule = new Rule(Guid.NewGuid(), MatchTarget.ProcessName, MatchKind.Exact, string.Empty, RuleVerdict.Block);
        Rules.Add(new RuleEditorViewModel(rule, RemoveRule));
    }

    [RelayCommand]
    private void AddUnlockOption()
    {
        UnlockOptions.Add(new UnlockOptionEditorViewModel(UnlockOption.CreateOffDay(), RemoveUnlockOption));
    }

    /// <summary>Adds a rule that was saved elsewhere (the classify prompt) without touching unsaved edits.</summary>
    public void AddSavedRule(Rule rule)
    {
        Rules.Add(new RuleEditorViewModel(rule, RemoveRule));
    }

    /// <summary>Rules copied in from another profile (unsaved until the tab is saved).</summary>
    public void AddCopiedRules(IEnumerable<Rule> rules)
    {
        foreach (Rule rule in rules)
        {
            Rules.Add(new RuleEditorViewModel(rule, RemoveRule));
        }
    }

    /// <summary>Takes rules out after they moved to another profile.</summary>
    public void RemoveRules(IEnumerable<RuleEditorViewModel> rules)
    {
        foreach (RuleEditorViewModel rule in rules.ToList())
        {
            Rules.Remove(rule);
        }
    }

    public void ClearRuleSelection()
    {
        AllRulesSelected = false;
    }

    /// <summary>
    /// A new profile named <paramref name="name"/> with this one's settings (grace period, unlisted sites, unlock
    /// options, on-screen timer, Cold Turkey blocks) and no rules yet.
    /// </summary>
    public ProfileEditorViewModel NewWithSameSettings(string name, IEnumerable<string> coldTurkeyBlocks)
    {
        int graceSeconds = int.TryParse(GraceSeconds.Trim(), out int typed) && typed >= 0 ? typed : DefaultGraceSeconds;
        var profile = new FocusProfile(Guid.NewGuid(), name, Array.Empty<Rule>(), TimeSpan.FromSeconds(graceSeconds))
        {
            UnlockOptions = UnlockOptions.Select(option => option.ToNewOption()).ToList(),
            AskAboutUnlistedSites = AskAboutUnlistedSites,
        };
        ProfileEditorViewModel copy = FromProfile(profile, ToOverlay());
        copy.ShowColdTurkeyBlocks(coldTurkeyBlocks, ToExpectedBlocks());
        return copy;
    }

    public bool TryBuild([NotNullWhen(true)] out FocusProfile? profile, [NotNullWhen(false)] out string? error)
    {
        profile = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "every profile needs a name";
            return false;
        }

        if (!int.TryParse(GraceSeconds.Trim(), out int graceSeconds) || graceSeconds < 0)
        {
            error = $"'{Name}': grace period must be a whole number of seconds (0 or more)";
            return false;
        }

        foreach (RuleEditorViewModel rule in Rules)
        {
            string? ruleError = rule.Validate();
            if (ruleError is not null)
            {
                error = $"'{Name}': {ruleError}";
                return false;
            }
        }

        foreach (UnlockOptionEditorViewModel option in UnlockOptions)
        {
            string? optionError = option.Validate();
            if (optionError is not null)
            {
                error = $"'{Name}': {optionError}";
                return false;
            }
        }

        profile = new FocusProfile(
            Id,
            Name.Trim(),
            Rules.Select(rule => rule.ToRule()).ToList(),
            TimeSpan.FromSeconds(graceSeconds))
        {
            UnlockOptions = UnlockOptions.Select(option => option.ToOption()).ToList(),
            AskAboutUnlistedSites = AskAboutUnlistedSites,
        };
        error = null;
        return true;
    }

    private void RemoveRule(RuleEditorViewModel rule)
    {
        Rules.Remove(rule);
    }

    // The selection texts follow each rule's tick, and rules coming and going.
    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (RuleEditorViewModel rule in e.OldItems?.OfType<RuleEditorViewModel>() ?? [])
        {
            rule.PropertyChanged -= OnRulePropertyChanged;
        }

        foreach (RuleEditorViewModel rule in e.NewItems?.OfType<RuleEditorViewModel>() ?? [])
        {
            rule.PropertyChanged += OnRulePropertyChanged;
        }

        OnSelectionChanged();
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RuleEditorViewModel.IsSelected))
        {
            OnSelectionChanged();
        }
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedRules));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(AllRulesSelected));
    }

    private void RemoveUnlockOption(UnlockOptionEditorViewModel option)
    {
        UnlockOptions.Remove(option);
    }
}
