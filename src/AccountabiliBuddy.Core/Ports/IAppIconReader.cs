using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>Reads the icon of an app, for showing next to its name (e.g. in History).</summary>
public interface IAppIconReader
{
    /// <summary>
    /// The icon of a running app with this process name (without .exe), or null when none is running or its
    /// program file can't be read.
    /// </summary>
    IconPixels? ReadIcon(string processName);
}
