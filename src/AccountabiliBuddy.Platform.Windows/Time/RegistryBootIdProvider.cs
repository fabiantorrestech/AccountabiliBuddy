using System.Security;
using AccountabiliBuddy.Core.Ports;
using Microsoft.Win32;

namespace AccountabiliBuddy.Platform.Windows.Time;

/// <summary>
/// The boot counter Windows keeps in HKLM\...\Memory Management\PrefetchParameters\BootId; it goes up by one with
/// every boot. Undocumented, so a missing or unreadable value gives null and the app simply trusts the clock at launch.
/// </summary>
public sealed class RegistryBootIdProvider : IBootIdProvider
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters";
    private const string ValueName = "BootId";

    public string? GetBootId()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: false);
            return key?.GetValue(ValueName) is int bootId ? bootId.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
