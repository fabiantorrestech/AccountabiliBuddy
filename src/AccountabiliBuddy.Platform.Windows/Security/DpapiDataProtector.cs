using System.Runtime.InteropServices;
using System.Text;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Cryptography;

namespace AccountabiliBuddy.Platform.Windows.Security;

/// <summary>
/// Encrypts with the Windows Data Protection API (DPAPI) in the current user's scope: only this Windows account on
/// this computer can decrypt. Calls crypt32 directly (via CsWin32) instead of adding the
/// System.Security.Cryptography.ProtectedData package.
/// An app-specific "entropy" value keeps other programs of the same user from decrypting it by accident.
/// </summary>
public sealed class DpapiDataProtector : IDataProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AccountabiliBuddy.ActivityLog.v1");

    public unsafe byte[] Protect(string text)
    {
        byte[] plain = Encoding.UTF8.GetBytes(text);
        fixed (byte* plainBytes = plain)
        fixed (byte* entropyBytes = Entropy)
        {
            var input = new CRYPT_INTEGER_BLOB { cbData = (uint)plain.Length, pbData = plainBytes };
            var entropy = new CRYPT_INTEGER_BLOB { cbData = (uint)Entropy.Length, pbData = entropyBytes };
            CRYPT_INTEGER_BLOB output = default;

            if (!PInvoke.CryptProtectData(&input, null, &entropy, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, &output))
            {
                throw new CryptographicFailureException("Windows could not encrypt the data.", Marshal.GetLastWin32Error());
            }

            return CopyAndFree(output);
        }
    }

    public unsafe string? Unprotect(byte[] data)
    {
        fixed (byte* dataBytes = data)
        fixed (byte* entropyBytes = Entropy)
        {
            var input = new CRYPT_INTEGER_BLOB { cbData = (uint)data.Length, pbData = dataBytes };
            var entropy = new CRYPT_INTEGER_BLOB { cbData = (uint)Entropy.Length, pbData = entropyBytes };
            CRYPT_INTEGER_BLOB output = default;

            if (!PInvoke.CryptUnprotectData(&input, null, &entropy, null, null, PInvoke.CRYPTPROTECT_UI_FORBIDDEN, &output))
            {
                return null; // Not ours: another user, another computer, or corrupted.
            }

            return Encoding.UTF8.GetString(CopyAndFree(output));
        }
    }

    private static unsafe byte[] CopyAndFree(CRYPT_INTEGER_BLOB blob)
    {
        try
        {
            return new Span<byte>(blob.pbData, (int)blob.cbData).ToArray();
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)blob.pbData);
        }
    }
}
