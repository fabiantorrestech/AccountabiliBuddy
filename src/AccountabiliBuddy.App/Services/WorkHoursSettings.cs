namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Work hours on this computer (ADR 27): during the day plan's work parts, the listed apps wait. App-owned (documents
/// "workHours"), off by default, offered on a personal computer.
/// </summary>
/// <param name="Apps">Process names (lowercase, without .exe) that are minimized during work hours.</param>
/// <param name="StoppedOn">The day the guard was stopped for (wait and reason), so a restart doesn't guard again.</param>
public sealed record WorkHoursSettings(bool On, IReadOnlyList<string> Apps, DateOnly? StoppedOn = null)
{
    /// <summary>Claude Code, terminals and IDEs. Browsers, mail and calls are never on the list by default.</summary>
    public static IReadOnlyList<string> DefaultApps { get; } =
    [
        "claude", "windowsterminal", "wt", "pwsh", "powershell", "cmd", "code", "cursor", "devenv", "rider64", "idea64",
        "pycharm64", "clion64", "webstorm64", "studio64",
    ];

    public static WorkHoursSettings Default { get; } = new(false, DefaultApps);
}
