namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Copies what is on screen into memory. Used for the blurred backdrop behind the quick-session popup;
/// images are never saved or sent anywhere.
/// </summary>
public interface IScreenCapture
{
    /// <returns>The pixels inside <paramref name="area"/>, or null if the screen could not be read.</returns>
    ScreenImage? Capture(PixelBox area);
}
