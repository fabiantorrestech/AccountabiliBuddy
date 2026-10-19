namespace AccountabiliBuddy.Core.Ports;

/// <summary>Registers the app to launch when the user signs in.</summary>
public interface IStartupRegistrar
{
    bool IsEnabled { get; }

    /// <summary>
    /// If launch at sign-in is registered for another copy of the app (e.g. a build folder or an older install),
    /// points it at this copy. Does nothing when it isn't registered at all.
    /// </summary>
    /// <returns>True if the registration was changed.</returns>
    bool RepairIfMoved();

    void Enable();

    void Disable();
}
