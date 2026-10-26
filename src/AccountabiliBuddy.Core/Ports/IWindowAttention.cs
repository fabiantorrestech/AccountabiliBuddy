namespace AccountabiliBuddy.Core.Ports;

/// <summary>Asks for attention without taking focus: the taskbar button flashes until the window is brought forward.</summary>
public interface IWindowAttention
{
    void Flash(nint windowHandle);
}
