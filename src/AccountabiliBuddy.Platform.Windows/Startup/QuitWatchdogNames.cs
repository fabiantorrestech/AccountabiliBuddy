namespace AccountabiliBuddy.Platform.Windows.Startup;

/// <summary>The named objects the app and its watchdog share (ADR 59). "Local\" scopes them to this Windows session.</summary>
internal static class QuitWatchdogNames
{
    public static string App(string instanceName) => $@"Local\{instanceName}";

    public static string Alive(string instanceName) => $@"Local\{instanceName}-QuitWatchdog-Alive";

    public static string Locked(string instanceName) => $@"Local\{instanceName}-QuitWatchdog-Locked";

    public static string StandDown(string instanceName) => $@"Local\{instanceName}-QuitWatchdog-StandDown";

    public static string AppStarted(string instanceName) => $@"Local\{instanceName}-QuitWatchdog-AppStarted";
}
