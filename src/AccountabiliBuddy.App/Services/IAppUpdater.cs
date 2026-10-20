namespace AccountabiliBuddy.App.Services;

/// <summary>Finds, downloads and installs a newer version of the app (ADR 12).</summary>
public interface IAppUpdater
{
    /// <summary>False for dev builds and test copies, which never update.</summary>
    bool IsInstalled { get; }

    /// <summary>The newer version available, or null when this one is the latest.</summary>
    Task<string?> CheckAsync();

    /// <summary>Downloads the version found by <see cref="CheckAsync"/>; progress is 0–100.</summary>
    Task DownloadAsync(Action<int> progress);

    /// <summary>Closes the app, installs the downloaded version and starts it again.</summary>
    /// <param name="minimized">Start again in the tray instead of showing the window.</param>
    void InstallAndRestart(bool minimized);
}
