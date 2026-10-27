using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Relay;

/// <summary>
/// The relay's admission key: a random secret made on the first run and kept in <c>relay-admission.key</c> next to the
/// certificate. Making a NEW group needs it (header <see cref="RelayLimits.AdmissionHeader"/>); a group that already
/// exists keeps working with its bearer token alone. Without it, anyone who can reach the relay could claim groups and
/// store files on it. It is printed with the relay's address and pin and travels in the join link (<c>k=</c>).
/// </summary>
internal sealed class AdmissionKey
{
    private const string FileName = "relay-admission.key";
    private const int KeyBytes = 32;

    private readonly byte[] _bytes;

    private AdmissionKey(string value)
    {
        Value = value;
        _bytes = Encoding.ASCII.GetBytes(value);
    }

    /// <summary>The key as 43 characters of base64url (no padding).</summary>
    public string Value { get; }

    public static AdmissionKey LoadOrCreate(string dataDirectory, out bool created)
    {
        string path = Path.Combine(dataDirectory, FileName);
        created = !File.Exists(path);
        if (created)
        {
            string made = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            WritePrivateFile(path, Encoding.ASCII.GetBytes(made));
        }

        return new AdmissionKey(File.ReadAllText(path, Encoding.ASCII).Trim());
    }

    /// <summary>Compared in constant time; a missing or wrong-length value never matches.</summary>
    public bool Matches(string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        byte[] candidate = Encoding.ASCII.GetBytes(provided.Trim());
        return candidate.Length == _bytes.Length && CryptographicOperations.FixedTimeEquals(candidate, _bytes);
    }

    // Readable by its owner only from the first byte on (Unix mode 0600); on Windows the data folder's ACL does that.
    private static void WritePrivateFile(string path, byte[] bytes)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(path, options);
        stream.Write(bytes);
    }
}
