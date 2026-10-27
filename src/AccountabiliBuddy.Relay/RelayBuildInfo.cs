using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;

namespace AccountabiliBuddy.Relay;

/// <summary>
/// What this relay is built from and runs on, printed at start and by <c>--version</c>. The relay is the only service of
/// the app that faces the network, and a self-contained build or a Docker image carries its own .NET runtime, which the
/// server's package updates do not touch: this line is how to check that the runtime is a current one.
/// </summary>
internal static class RelayBuildInfo
{
    public static string Describe()
    {
        return $"AccountabiliBuddy relay {AppVersion()}, {RuntimeInformation.FrameworkDescription} " +
            $"(runtime {Environment.Version}, ASP.NET Core {AspNetVersion()}), {RuntimeInformation.RuntimeIdentifier}";
    }

    private static string AppVersion()
    {
        return Clean(typeof(RelayBuildInfo).Assembly);
    }

    private static string AspNetVersion()
    {
        return Clean(typeof(WebApplication).Assembly);
    }

    // The informational version is "1.2.3+<commit hash>"; the hash is noise here.
    private static string Clean(Assembly assembly)
    {
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        int plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }
}
