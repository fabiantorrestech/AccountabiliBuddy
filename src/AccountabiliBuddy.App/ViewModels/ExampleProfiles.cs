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

        return new FocusProfile(Guid.NewGuid(), "Coding (example)", rules, TimeSpan.FromSeconds(30))
        {
            UnlockOptions = new[] { UnlockOption.CreateOffDay() },
        };
    }

    /// <summary>
    /// "QS - Default": what "Quick Start with Default" starts, made on every install. Allows the Windows basics, Office
    /// and chat tools and developer tools; blocks common distractions. Delete what doesn't fit in Profiles.
    /// </summary>
    public static FocusProfile QuickStartDefault(string name)
    {
        var rules = new List<Rule>
        {
            // Windows basics.
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "explorer", "File Explorer"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "notepad", "Notepad"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "CalculatorApp", "Calculator"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "SystemSettings", "Settings"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "Taskmgr", "Task Manager"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "SnippingTool", "Snipping Tool"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "WindowsTerminal", "Windows Terminal"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "AccountabiliBuddy.App", "AccountabiliBuddy"),

            // Office and chat.
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "WINWORD", "Word"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "EXCEL", "Excel"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "POWERPNT", "PowerPoint"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "OUTLOOK", "Outlook (classic)"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "olk", "Outlook (new)"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "ONENOTE", "OneNote"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "ms-teams", "Teams"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "slack", "Slack"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "Zoom", "Zoom"),

            // Developer tools.
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "Code", "VS Code"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "devenv", "Visual Studio"),
            Allow(MatchTarget.ProcessName, MatchKind.Exact, "rider64", "JetBrains Rider"),
            Allow(MatchTarget.Url, MatchKind.Domain, "github.com", null),
            Allow(MatchTarget.Url, MatchKind.Domain, "stackoverflow.com", null),
            Allow(MatchTarget.Url, MatchKind.Domain, "learn.microsoft.com", null),

            // Common distractions.
            Block(MatchTarget.Url, MatchKind.Prefix, "youtube.com/shorts", "Shorts only; other YouTube is unlisted"),
            Block(MatchTarget.Url, MatchKind.Domain, "reddit.com", null),
            Block(MatchTarget.Url, MatchKind.Domain, "x.com", null),
            Block(MatchTarget.Url, MatchKind.Domain, "twitter.com", null),
            Block(MatchTarget.Url, MatchKind.Domain, "instagram.com", null),
            Block(MatchTarget.Url, MatchKind.Domain, "tiktok.com", null),
            Block(MatchTarget.Url, MatchKind.Domain, "facebook.com", null),
            Block(MatchTarget.Url, MatchKind.Domain, "netflix.com", null),
            Block(MatchTarget.Url, MatchKind.Domain, "twitch.tv", null),
            Block(MatchTarget.ProcessName, MatchKind.Exact, "steam", null),
        };

        return new FocusProfile(Guid.NewGuid(), name, rules, TimeSpan.FromSeconds(30))
        {
            UnlockOptions = new[] { UnlockOption.CreateOffDay() },
        };
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
