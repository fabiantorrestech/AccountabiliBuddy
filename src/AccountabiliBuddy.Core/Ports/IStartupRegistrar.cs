namespace AccountabiliBuddy.Core.Ports;

/// <summary>Registers the app to launch when the user signs in.</summary>
public interface IStartupRegistrar
{
    bool IsEnabled { get; }

    void Enable();

    void Disable();
}
