using System.ComponentModel;
using System.Diagnostics;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Network;

/// <summary>
/// The relay's inbound Windows Firewall rule, kept with netsh. Looking the rule up needs no rights; adding or removing
/// it runs netsh once through cmd with "runas", which shows Windows' permission prompt. The rule is per program (any port,
/// TCP), so changing the relay's port needs no new prompt.
/// </summary>
public sealed class WindowsRelayFirewall : IRelayFirewall
{
    private const string RuleName = "AccountabiliBuddy relay";
    private const int ElevationDeclined = 1223;

    public bool HasRule(string executablePath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{RuleName}\" verbose")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (process is null)
            {
                return false;
            }

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return false;
            }

            // Exit code 1 = no such rule. The program's path is the one part of the output that isn't translated.
            return process.ExitCode == 0 && output.Contains(executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    public async Task<FirewallResult> AllowAsync(string executablePath, bool includePublicNetworks)
    {
        if (!IsSafeForCommandLine(executablePath))
        {
            return FirewallResult.Failed;
        }

        string profiles = includePublicNetworks ? "private,domain,public" : "private,domain";
        string commands = $"netsh advfirewall firewall delete rule name=\"{RuleName}\" & "
            + $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{executablePath}\" "
            + $"enable=yes profile={profiles} protocol=TCP";
        FirewallResult result = await RunElevatedAsync(commands);
        return result == FirewallResult.Done && !HasRule(executablePath) ? FirewallResult.Failed : result;
    }

    public async Task<FirewallResult> RemoveAsync(string executablePath)
    {
        FirewallResult result = await RunElevatedAsync($"netsh advfirewall firewall delete rule name=\"{RuleName}\"");
        return result == FirewallResult.Done && HasRule(executablePath) ? FirewallResult.Failed : result;
    }

    // The path goes into a cmd command line, where these characters would be read as cmd syntax even inside quotes.
    private static bool IsSafeForCommandLine(string path)
    {
        return Path.IsPathFullyQualified(path) && path.IndexOfAny(['"', '%', '^', '!']) < 0;
    }

    private static async Task<FirewallResult> RunElevatedAsync(string commands)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{commands}\"")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
            {
                return FirewallResult.Failed;
            }

            await process.WaitForExitAsync();
            return FirewallResult.Done;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ElevationDeclined)
        {
            return FirewallResult.Declined;
        }
        catch (Win32Exception)
        {
            return FirewallResult.Failed;
        }
    }
}
