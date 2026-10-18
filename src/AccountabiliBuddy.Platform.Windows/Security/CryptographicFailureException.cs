namespace AccountabiliBuddy.Platform.Windows.Security;

/// <summary>DPAPI refused to encrypt. Carries the Windows error code for diagnostics.</summary>
public sealed class CryptographicFailureException : Exception
{
    public CryptographicFailureException(string message, int windowsErrorCode)
        : base($"{message} (Windows error {windowsErrorCode})")
    {
        WindowsErrorCode = windowsErrorCode;
    }

    public int WindowsErrorCode { get; }
}
