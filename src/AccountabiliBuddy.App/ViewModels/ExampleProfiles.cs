using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Starter profile so a new user can try a session without writing rules from scratch.</summary>
public static class ExampleProfiles
{
    public static FocusProfile Coding()
    {
        var rules = new List<Rule>
        {
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "rider64", "JetBrains Rider"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "devenv", "Visual Studio"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "Code", "VS Code"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "WindowsTerminal", null),
            Allow(MatchTarget.Url, MatchKind.Domain, "github.com", null),
            Allow(MatchTarget.Url, MatchKind.Domain, "stackoverflow.com", null),
            Allow(MatchTarget.Url, MatchKind.Domain, "learn.microsoft.com", null),
            Block(MatchTarget.Url, MatchKind.Prefix, "youtube.com/shorts", "Shorts only; other YouTube is unlisted"),
            Block(MatchTarget.Url, MatchKind.Domain, "reddit.com", null),
            Block(MatchTarget.Url, MatchKind.Domain, "x.com", null),
            Block(MatchTarget.Url, MatchKind.Domain, "instagram.com", null),
            Block(MatchTarget.ProcessName, MatchKind.Exact, "steam", null),
        };

        return new FocusProfile(Guid.NewGuid(), "Coding (example)", rules, TimeSpan.FromSeconds(30));
    }

    private static Rule Allow(MatchTarget target, MatchKind kind, string pattern, string? note)
    {
        return new Rule(Guid.NewGuid(), target, kind, pattern, RuleVerdict.Allow, note);
    }

    private static Rule Block(MatchTarget target, MatchKind kind, string pattern, string? note)
    {
        return new Rule(Guid.NewGuid(), target, kind, pattern, RuleVerdict.Block, note);
    }
}
