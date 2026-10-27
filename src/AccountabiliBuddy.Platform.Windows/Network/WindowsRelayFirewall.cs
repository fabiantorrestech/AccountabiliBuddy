using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Network;

/// <summary>
/// The relay's inbound Windows Firewall rule, kept with netsh. Looking the rule up needs no rights; adding or removing
/// it runs netsh once through cmd with "runas", which shows Windows' permission prompt. The rule is per program AND per
/// local port (TCP): a program that someone swapped for another could otherwise open every port, so changing the relay's
/// port asks for a new permission. netsh and cmd are started from the system folder, never found through PATH.
/// </summary>
public sealed class WindowsRelayFirewall : IRelayFirewall
{
    private const string RuleName = "AccountabiliBuddy relay";
    private const int ElevationDeclined = 1223;

    private static string Netsh => Path.Combine(Environment.SystemDirectory, "netsh.exe");

    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    public bool HasRule(string executablePath, int port)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Netsh, $"advfirewall firewall show rule name=\"{RuleName}\" verbose")
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

            // Exit code 1 = no such rule. The program's path and the port number are the parts of the output that aren't
            // translated: the port is the value at the end of a "label: value" line.
            return process.ExitCode == 0
                && output.Contains(executablePath, StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(output, $@"(?m):\s+{port}\s*$");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    public async Task<FirewallResult> AllowAsync(string executablePath, int port, bool includePublicNetworks)
    {
        if (!IsSafeForCommandLine(executablePath) || !IsSafeForCommandLine(Netsh))
        {
            return FirewallResult.Failed;
        }

        string profiles = includePublicNetworks ? "private,domain,public" : "private,domain";
        string commands = $"\"{Netsh}\" advfirewall firewall delete rule name=\"{RuleName}\" & "
            + $"\"{Netsh}\" advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{executablePath}\" "
            + $"enable=yes profile={profiles} protocol=TCP localport={port}";
        FirewallResult result = await RunElevatedAsync(commands);
        return result == FirewallResult.Done && !HasRule(executablePath, port) ? FirewallResult.Failed : result;
    }

    public async Task<FirewallResult> RemoveAsync(string executablePath, int port)
    {
        if (!IsSafeForCommandLine(Netsh))
        {
            return FirewallResult.Failed;
        }

        FirewallResult result = await RunElevatedAsync($"\"{Netsh}\" advfirewall firewall delete rule name=\"{RuleName}\"");
        return result == FirewallResult.Done && HasRule(executablePath, port) ? FirewallResult.Failed : result;
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
            using var process = Process.Start(new ProcessStartInfo(Cmd, $"/c \"{commands}\"")
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
