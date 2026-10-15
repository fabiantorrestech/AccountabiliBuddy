using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>Reads on-screen text of a window (OCR). Only called when rules cannot decide.</summary>
public interface IScreenTextReader
{
    /// <returns>Recognized text, or null if the window could not be captured.</returns>
    Task<string?> ReadTextAsync(ActivitySnapshot snapshot, CancellationToken cancellationToken = default);
}
