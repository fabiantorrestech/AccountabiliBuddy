namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Encrypts sensitive text at rest so only the same user on the same computer can read it back
/// (DPAPI on Windows; ADR 6).
/// </summary>
public interface IDataProtector
{
    byte[] Protect(string text);

    /// <returns>The original text, or null if it cannot be decrypted (e.g. copied from another user or computer).</returns>
    string? Unprotect(byte[] data);
}
