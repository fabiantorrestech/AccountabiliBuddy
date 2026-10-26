using System.Diagnostics;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Integrations;

/// <summary>
/// Runs Cold Turkey Blocker's command line (ADR 37, 39): "-start "block" [-lock minutes | -password p]" and
/// "-stop "block" [-password p]", one at a time, off the UI thread, with no window. Cold Turkey answers 0 even when
/// it refuses (a locked block), so true only means the command ran; the caller reads the settings again to confirm.
/// Its settings file is never written.
/// </summary>
public sealed class ColdTurkeyCommandLine : IColdTurkeyControl
{
    private const string Executable = "Cold Turkey Blocker.exe";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Func<string?> _installFolder;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <param name="installFolder">Cold Turkey's install folder (from <see cref="ColdTurkeyDetector"/>), or null.</param>
    public ColdTurkeyCommandLine(Func<string?> installFolder)
    {
        _installFolder = installFolder;
    }

    public bool IsAvailable => ExecutablePath() is not null;

    public Task<bool> StartAsync(string block, int? lockMinutes = null, string? password = null)
    {
        var arguments = new List<string> { "-start", block };
        if (password is not null)
        {
            arguments.AddRange(new[] { "-password", password });
        }
        else if (lockMinutes is int minutes and > 0)
        {
            arguments.AddRange(new[] { "-lock", minutes.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        return RunAsync(arguments);
    }

    public Task<bool> StopAsync(string block, string? password = null)
    {
        var arguments = new List<string> { "-stop", block };
        if (password is not null)
        {
            arguments.AddRange(new[] { "-password", password });
        }

        return RunAsync(arguments);
    }

    private string? ExecutablePath()
    {
        string? folder = _installFolder();
        string? path = folder is null ? null : Path.Combine(folder, Executable);
        return path is not null && File.Exists(path) ? path : null;
    }

    private async Task<bool> RunAsync(IReadOnlyList<string> arguments)
    {
        string? path = ExecutablePath();
        if (path is null)
        {
            return false;
        }

        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            using var cancel = new CancellationTokenSource(Timeout);
            try
            {
                await process.WaitForExitAsync(cancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }
}
