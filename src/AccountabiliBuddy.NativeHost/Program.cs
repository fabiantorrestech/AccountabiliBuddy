using System.Security.Principal;
using AccountabiliBuddy.Core.Browser;

namespace AccountabiliBuddy.NativeHost;

/// <summary>
/// Entry point for the browser native messaging host. The browser starts one host process per extension
/// connection and talks to it over stdin/stdout; the host relays every message to the app's named pipe and back.
/// Nothing but framed messages may ever be written to stdout.
/// </summary>
public static class Program
{
    public static async Task<int> Main()
    {
        string userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current user has no SID.");

        await using Stream fromBrowser = Console.OpenStandardInput();
        await using Stream toBrowser = Console.OpenStandardOutput();

        var relay = new HostRelay(fromBrowser, toBrowser, BrowserBridge.PipeName(userSid));
        await relay.RunAsync();
        return 0;
    }
}
