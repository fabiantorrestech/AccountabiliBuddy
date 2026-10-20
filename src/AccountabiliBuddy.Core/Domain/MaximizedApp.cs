namespace AccountabiliBuddy.Core.Domain;

/// <summary>A maximized app window in front: which app (process name, without .exe) and its whole screen.</summary>
public sealed record MaximizedApp(string ProcessName, ScreenBox Screen);
