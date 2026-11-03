using Velopack;
using Velopack.Sources;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Updates through Velopack from the public releases repository on GitHub (ADR 12): no token, read-only. The
/// feed and packages are read from the latest release's download address, not api.github.com, so the check isn't
/// subject to the API's 60-requests-per-hour limit per IP. The download is checked against the release feed's
/// checksum before it is installed.
/// </summary>
public sealed class VelopackUpdater : IAppUpdater
{
    /// <summary>Holds only installers and update packages, never source code.</summary>
    public const string ReleasesRepository = "https://github.com/fabiantorrestech/AccountabiliBuddy-releases";

    /// <summary>Where the latest release's files (releases.win.json and the packages it lists) are served from.</summary>
    private const string LatestReleaseDownloadAddress = ReleasesRepository + "/releases/latest/download/";

    // Deltas stay off: this address holds only the latest release, and Velopack would apply its one delta on top of an
    // install two or more versions behind without checking the base. A full package is larger but has its checksum verified.
    private readonly UpdateManager _manager = new(new SimpleWebSource(LatestReleaseDownloadAddress), new UpdateOptions { MaximumDeltasBeforeFallback = -1 });
    private UpdateInfo? _found;

    /// <summary>Runs just before the update closes the app: the quit watchdog (ADR 59) stands down, so it can't hold the files open or bring the app back.</summary>
    public Action? BeforeRestart { get; set; }

    public bool IsInstalled => AppPaths.IsInstalled && _manager.IsInstalled;

    public async Task<string?> CheckAsync()
    {
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(Action<int> progress)
    {
        return _found is null ? Task.CompletedTask : _manager.DownloadUpdatesAsync(_found, progress);
    }

    public void InstallAndRestart(bool minimized)
    {
        if (_found is null)
        {
            return;
        }

        BeforeRestart?.Invoke();
        _manager.ApplyUpdatesAndRestart(_found.TargetFullRelease, minimized ? new[] { StartupOptions.MinimizedArgument } : Array.Empty<string>());
    }
}
