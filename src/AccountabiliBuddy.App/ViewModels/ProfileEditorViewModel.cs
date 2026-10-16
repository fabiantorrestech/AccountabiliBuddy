using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Editable copy of one <see cref="FocusProfile"/>. Changes apply only when the Profiles tab is saved.</summary>
public sealed partial class ProfileEditorViewModel : ObservableObject
{
    private const int DefaultGraceSeconds = 30;

    private ProfileEditorViewModel(FocusProfile profile)
    {
        Id = profile.Id;
        Name = profile.Name;
        GraceSeconds = ((int)profile.UnknownGracePeriod.TotalSeconds).ToString();

        foreach (Rule rule in profile.Rules)
        {
            Rules.Add(new RuleEditorViewModel(rule, RemoveRule));
        }
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>How long unknown activity is tolerated before asking "is this distracting?".</summary>
    [ObservableProperty]
    public partial string GraceSeconds { get; set; }

    public ObservableCollection<RuleEditorViewModel> Rules { get; } = new();

    public static ProfileEditorViewModel FromProfile(FocusProfile profile)
    {
        return new ProfileEditorViewModel(profile);
    }

    public static ProfileEditorViewModel CreateNew()
    {
        var profile = new FocusProfile(
            Guid.NewGuid(),
            "New profile",
            Array.Empty<Rule>(),
            TimeSpan.FromSeconds(DefaultGraceSeconds));
        return new ProfileEditorViewModel(profile);
    }

    [RelayCommand]
    private void AddRule()
    {
        var rule = new Rule(Guid.NewGuid(), MatchTarget.ProcessName, MatchKind.Exact, string.Empty, RuleVerdict.Block);
        Rules.Add(new RuleEditorViewModel(rule, RemoveRule));
    }

    /// <summary>Adds a rule that was saved elsewhere (the classify prompt) without touching unsaved edits.</summary>
    public void AddSavedRule(Rule rule)
    {
        Rules.Add(new RuleEditorViewModel(rule, RemoveRule));
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

        profile = new FocusProfile(
            Id,
            Name.Trim(),
            Rules.Select(rule => rule.ToRule()).ToList(),
            TimeSpan.FromSeconds(graceSeconds));
        error = null;
        return true;
    }

    private void RemoveRule(RuleEditorViewModel rule)
    {
        Rules.Remove(rule);
    }
}
