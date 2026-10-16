using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One editable rule row in the Profiles tab.</summary>
public sealed partial class RuleEditorViewModel : ObservableObject
{
    private readonly Action<RuleEditorViewModel> _remove;

    public RuleEditorViewModel(Rule rule, Action<RuleEditorViewModel> remove)
    {
        _remove = remove;
        Id = rule.Id;
        Target = rule.Target;
        Kind = rule.Kind;
        Pattern = rule.Pattern;
        Verdict = rule.Verdict;
        Note = rule.Note ?? string.Empty;
    }

    public static IReadOnlyList<MatchTarget> Targets { get; } = Enum.GetValues<MatchTarget>();

    public static IReadOnlyList<MatchKind> Kinds { get; } = Enum.GetValues<MatchKind>();

    public static IReadOnlyList<RuleVerdict> Verdicts { get; } = Enum.GetValues<RuleVerdict>();

    public Guid Id { get; }

    [ObservableProperty]
    public partial MatchTarget Target { get; set; }

    [ObservableProperty]
    public partial MatchKind Kind { get; set; }

    [ObservableProperty]
    public partial string Pattern { get; set; }

    [ObservableProperty]
    public partial RuleVerdict Verdict { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [RelayCommand]
    private void Remove()
    {
        _remove(this);
    }

    /// <returns>An error message, or null if the rule is valid.</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Pattern))
        {
            return "a rule has an empty pattern";
        }

        if (Kind == MatchKind.Domain && Target != MatchTarget.Url)
        {
            return $"'Domain' matching only works on Url rules (pattern '{Pattern}')";
        }

        if (Kind == MatchKind.Regex && !IsValidRegex(Pattern))
        {
            return $"'{Pattern}' is not a valid regular expression";
        }

        return null;
    }

    public Rule ToRule()
    {
        string? note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        return new Rule(Id, Target, Kind, Pattern.Trim(), Verdict, note);
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
