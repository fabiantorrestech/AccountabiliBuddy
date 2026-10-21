using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The link code (ADR 22): the sync key's 200 random bits as 40 Crockford base32 characters (0-9 and A-Z without I, L,
/// O and U), most significant bit first, shown in groups of 5. Whoever has it can read and write the group's files, so
/// it's stored only under DPAPI (PC) and TokenVault (phone).
/// </summary>
public static class SyncKeyCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int CodeLength = 40;
    private const int BitsPerCharacter = 5;
    private const int CharacterMask = (1 << BitsPerCharacter) - 1;
    private const int GroupLength = 5;

    /// <returns>A new code: 40 characters from 25 random bytes.</returns>
    public static string NewCode()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(SyncKey.SecretBytes);
        try
        {
            return Encode(secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>
    /// Reads a typed or scanned code. Spaces, dashes and lower case are ignored; what's left must be exactly 40
    /// characters of the alphabet (I, L, O and U are refused, not read as 1 or 0).
    /// </summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out SyncKey? key)
    {
        key = null;
        string code = Normalize(text);
        if (code.Length != CodeLength)
        {
            return false;
        }

        byte[] secret = new byte[SyncKey.SecretBytes];
        try
        {
            int buffer = 0;
            int bufferedBits = 0;
            int written = 0;
            foreach (char character in code)
            {
                int value = Alphabet.IndexOf(character);
                if (value < 0)
                {
                    return false;
                }

                buffer = (buffer << BitsPerCharacter) | value;
                bufferedBits += BitsPerCharacter;
                if (bufferedBits >= 8)
                {
                    bufferedBits -= 8;
                    secret[written++] = (byte)(buffer >> bufferedBits);
                    buffer &= (1 << bufferedBits) - 1;
                }
            }

            key = SyncKey.FromSecret(secret);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <returns>The code in groups of 5 joined by dashes, e.g. "8KQ2V-7XHPM-…".</returns>
    public static string Display(string code)
    {
        return string.Join('-', Normalize(code).Chunk(GroupLength).Select(group => new string(group)));
    }

    private static string Normalize(string text)
    {
        return new string(text.Where(character => character != '-' && !char.IsWhiteSpace(character))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string Encode(byte[] secret)
    {
        var code = new StringBuilder(CodeLength);
        int buffer = 0;
        int bufferedBits = 0;
        foreach (byte value in secret)
        {
            buffer = (buffer << 8) | value;
            bufferedBits += 8;
            while (bufferedBits >= BitsPerCharacter)
            {
                bufferedBits -= BitsPerCharacter;
                code.Append(Alphabet[(buffer >> bufferedBits) & CharacterMask]);
            }

            buffer &= (1 << bufferedBits) - 1;
        }

        return code.ToString();
    }
}
