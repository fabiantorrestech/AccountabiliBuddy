using AccountabiliBuddy.Core.Ports;
using Microsoft.Win32;

namespace AccountabiliBuddy.Platform.Windows.Startup;

/// <summary>
/// Launches the app at sign-in via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// Per-user, so no admin rights are needed.
/// </summary>
public sealed class RunKeyStartupRegistrar : IStartupRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AccountabiliBuddy";

    private readonly string _command;

    /// <param name="executablePath">Full path of the app executable.</param>
    /// <param name="arguments">Arguments passed at startup, e.g. "--minimized".</param>
    public RunKeyStartupRegistrar(string executablePath, string arguments)
    {
        _command = $"\"{executablePath}\" {arguments}".TrimEnd();
    }

    public bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string registered
                && string.Equals(registered, _command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Enable()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, _command, RegistryValueKind.String);
    }

    public bool RepairIfMoved()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key?.GetValue(ValueName) is not string registered
            || string.Equals(registered, _command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Enable();
        return true;
    }

    public void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
