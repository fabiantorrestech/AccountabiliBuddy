using System.Globalization;
using System.Security.Cryptography;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A sealed sync file as QR codes the phone's own camera opens (ADR 22): links
/// "accountabilibuddy://sync?g=&lt;group id&gt;&amp;f=&lt;check&gt;&amp;p=&lt;i&gt;-&lt;n&gt;&amp;d=&lt;part&gt;", one per code. The check is
/// the first 4 bytes of the whole file's SHA-256 in hex: it tells the codes of one file from those of another (the PC
/// draws new codes each time) and proves the parts were put back together right. The part is base64url without
/// padding. <see cref="SyncQrCollector"/> reads them back, in any order; the phone does the same (android/shared).
/// </summary>
public static class SyncQr
{
    /// <summary>The start of every link, as written; a reader takes its letters in any case.</summary>
    public const string LinkStart = "accountabilibuddy://sync?";

    /// <summary>Sealed bytes per code: a link of about 1,650 characters, which a phone reads off a screen.</summary>
    public const int BytesPerCode = 1200;

    /// <summary>The most codes the PC shows for one file (<see cref="SyncFileBuilder.ForQr"/> fits a day into them).</summary>
    public const int MaxCodes = 4;

    /// <summary>The most parts a file can be cut into (two digits in a link).</summary>
    public const int MaxParts = 99;

    private const int CheckBytes = 4;

    /// <returns>The links in order, from 1 to n.</returns>
    /// <exception cref="ArgumentException">
    /// The bytes are empty, the group id isn't 8 lower-case hex characters, or they'd need more than <see cref="MaxParts"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytesPerCode"/> is under 1.</exception>
    public static IReadOnlyList<string> Links(byte[] sealedBytes, string groupId, int maxBytesPerCode = BytesPerCode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytesPerCode, 1);
        if (sealedBytes.Length == 0)
        {
            throw new ArgumentException("There are no bytes to show.", nameof(sealedBytes));
        }

        if (!IsHexId(groupId))
        {
            throw new ArgumentException("The group id is 8 lower-case hex characters.", nameof(groupId));
        }

        int total = (sealedBytes.Length + maxBytesPerCode - 1) / maxBytesPerCode;
        if (total > MaxParts)
        {
            throw new ArgumentException($"The file needs more than {MaxParts} codes.", nameof(sealedBytes));
        }

        string check = CheckOf(sealedBytes);
        var links = new List<string>(total);
        for (int index = 0; index < total; index++)
        {
            int start = index * maxBytesPerCode;
            byte[] part = sealedBytes[start..Math.Min(start + maxBytesPerCode, sealedBytes.Length)];
            links.Add(string.Create(CultureInfo.InvariantCulture,
                $"{LinkStart}g={groupId}&f={check}&p={index + 1}-{total}&d={ToBase64Url(part)}"));
        }

        return links;
    }

    /// <summary>The file's check: the first 4 bytes of its SHA-256, in lower-case hex.</summary>
    internal static string CheckOf(ReadOnlySpan<byte> sealedBytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(sealedBytes)[..CheckBytes]);
    }

    /// <summary>A group id or a check: 8 characters of 0-9 and a-f.</summary>
    internal static bool IsHexId(string text)
    {
        return text.Length == CheckBytes * 2 && text.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    }

    /// <returns>Base64url without padding.</returns>
    internal static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <returns>The bytes, or null when the text isn't base64url without padding.</returns>
    internal static byte[]? FromBase64Url(string text)
    {
        if (text.Length == 0 || text.Length % 4 == 1
            || !text.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return null;
        }

        string standard = text.Replace('-', '+').Replace('_', '/');
        try
        {
            return Convert.FromBase64String(standard + new string('=', (4 - standard.Length % 4) % 4));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
