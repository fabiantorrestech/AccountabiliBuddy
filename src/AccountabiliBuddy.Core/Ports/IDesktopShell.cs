namespace AccountabiliBuddy.Core.Ports;

/// <summary>Finds an app's program file and hands things to the desktop: show a file in the file manager, open a site in the browser.</summary>
public interface IDesktopShell
{
    /// <summary>The program file of a running app with this process name (without .exe), or null when none is running or it can't be read.</summary>
    string? FindRunningProgram(string processName);

    /// <summary>Opens the file manager at this file, with it selected.</summary>
    void ShowInFileManager(string path);

    /// <summary>Opens https://<paramref name="domain"/> in the default browser. Does nothing unless it is a plain host name.</summary>
    void OpenWebsite(string domain);
}
