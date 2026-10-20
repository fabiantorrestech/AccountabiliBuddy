using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Core.Link;

/// <summary>One-time pairing codes and device tokens (ADR 14): random, and tokens are kept only as hashes.</summary>
public static class LinkSecrets
{
    // No 0/O or 1/I/L, so a code typed by hand is easy to read.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;
    private const int TokenBytes = 32;

    public static string NewPairingCode()
    {
        return RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
    }

    public static string NewToken()
    {
        return ToBase64Url(RandomNumberGenerator.GetBytes(TokenBytes));
    }

    public static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>True when <paramref name="token"/> hashes to <paramref name="hash"/> (compared in constant time).</summary>
    public static bool Matches(string token, string hash)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(hash));
    }

    /// <summary>Compares a typed or scanned code with the offered one, ignoring case and spaces, in constant time.</summary>
    public static bool CodeMatches(string given, string offered)
    {
        string normalized = new(given.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(normalized), Encoding.ASCII.GetBytes(offered));
    }

    public static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
