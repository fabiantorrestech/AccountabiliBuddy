using Velopack;
using Velopack.Sources;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Updates through Velopack from the public releases repository on GitHub (ADR 12): no token, read-only. The
/// download is checked against the release feed's checksum before it is installed.
/// </summary>
public sealed class VelopackUpdater : IAppUpdater
{
    /// <summary>Holds only installers and update packages, never source code.</summary>
    public const string ReleasesRepository = "https://github.com/fabiantorrestech/AccountabiliBuddy-releases";

    private readonly UpdateManager _manager = new(new GithubSource(ReleasesRepository, null, false));
    private UpdateInfo? _found;

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

        _manager.ApplyUpdatesAndRestart(_found.TargetFullRelease, minimized ? new[] { StartupOptions.MinimizedArgument } : Array.Empty<string>());
    }
}
