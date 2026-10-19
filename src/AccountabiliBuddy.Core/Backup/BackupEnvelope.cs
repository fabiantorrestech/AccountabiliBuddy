using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Core.Backup;

/// <summary>
/// The outer layer of a backup file (*.abbackup): a small JSON object naming the format and how the content is
/// protected, then the content: readable JSON, AES-GCM ciphertext (key from the password with PBKDF2-SHA256), or
/// DPAPI-protected bytes for this computer only. Uses only .NET's built-in cryptography.
/// </summary>
public static class BackupEnvelope
{
    public const string Format = "accountabilibuddy-backup";
    public const int Version = 1;
    public const string FileExtension = ".abbackup";

    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes($"{Format}/{Version}");

    /// <param name="contentJson">The backup content (a JSON object).</param>
    /// <param name="password">Required for <see cref="BackupProtection.Password"/>.</param>
    /// <param name="protector">Required for <see cref="BackupProtection.ThisComputer"/>.</param>
    public static string Seal(string contentJson, BackupProtection protection, string? password = null, IDataProtector? protector = null)
    {
        var envelope = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["protection"] = protection.ToString(),
        };

        switch (protection)
        {
            case BackupProtection.None:
                envelope["content"] = JsonNode.Parse(contentJson);
                break;
            case BackupProtection.Password:
                if (string.IsNullOrEmpty(password))
                {
                    throw new ArgumentException("A password is required.", nameof(password));
                }

                byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
                byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
                byte[] plaintext = Encoding.UTF8.GetBytes(contentJson);
                byte[] ciphertext = new byte[plaintext.Length];
                byte[] tag = new byte[TagSize];
                using (var aes = new AesGcm(DeriveKey(password, salt), TagSize))
                {
                    aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
                }

                envelope["kdf"] = "PBKDF2-SHA256";
                envelope["iterations"] = Iterations;
                envelope["salt"] = Convert.ToBase64String(salt);
                envelope["nonce"] = Convert.ToBase64String(nonce);
                envelope["tag"] = Convert.ToBase64String(tag);
                envelope["data"] = Convert.ToBase64String(ciphertext);
                break;
            case BackupProtection.ThisComputer:
                envelope["data"] = Convert.ToBase64String((protector ?? throw new ArgumentNullException(nameof(protector))).Protect(contentJson));
                break;
        }

        return envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>How the file is protected, so the app knows whether to ask for a password.</summary>
    public static BackupProtection Inspect(string fileText)
    {
        JsonObject envelope = ParseEnvelope(fileText);
        return ProtectionOf(envelope);
    }

    /// <returns>The content JSON.</returns>
    /// <exception cref="FormatException">Not a backup file, or a newer version.</exception>
    /// <exception cref="BackupPasswordException">A password is needed, or it's wrong.</exception>
    public static string Open(string fileText, string? password = null, IDataProtector? protector = null)
    {
        JsonObject envelope = ParseEnvelope(fileText);
        switch (ProtectionOf(envelope))
        {
            case BackupProtection.None:
                return envelope["content"]?.ToJsonString() ?? throw new FormatException("The backup has no content.");
            case BackupProtection.Password:
                if (string.IsNullOrEmpty(password))
                {
                    throw new BackupPasswordException("This backup is password-protected. Enter its password.");
                }

                try
                {
                    int iterations = envelope["iterations"]?.GetValue<int>() ?? Iterations;
                    byte[] salt = Bytes(envelope, "salt");
                    byte[] nonce = Bytes(envelope, "nonce");
                    byte[] tag = Bytes(envelope, "tag");
                    byte[] ciphertext = Bytes(envelope, "data");
                    byte[] plaintext = new byte[ciphertext.Length];
                    using var aes = new AesGcm(DeriveKey(password, salt, iterations), TagSize);
                    aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
                    return Encoding.UTF8.GetString(plaintext);
                }
                catch (AuthenticationTagMismatchException ex)
                {
                    throw new BackupPasswordException("Wrong password (or the file was changed).", ex);
                }
            default:
                string? text = (protector ?? throw new ArgumentNullException(nameof(protector))).Unprotect(Bytes(envelope, "data"));
                return text ?? throw new FormatException("This backup was made for another Windows account or computer and can't be opened here.");
        }
    }

    private static JsonObject ParseEnvelope(string fileText)
    {
        JsonObject? envelope;
        try
        {
            envelope = JsonNode.Parse(fileText) as JsonObject;
        }
        catch (JsonException ex)
        {
            throw new FormatException("This isn't an AccountabiliBuddy backup file.", ex);
        }

        if (envelope is null || envelope["format"]?.GetValue<string>() != Format)
        {
            throw new FormatException("This isn't an AccountabiliBuddy backup file.");
        }

        if ((envelope["version"]?.GetValue<int>() ?? 0) > Version)
        {
            throw new FormatException("This backup was made by a newer version of AccountabiliBuddy. Update the app first.");
        }

        return envelope;
    }

    private static BackupProtection ProtectionOf(JsonObject envelope)
    {
        return Enum.TryParse(envelope["protection"]?.GetValue<string>(), out BackupProtection protection)
            ? protection
            : throw new FormatException("The backup's protection isn't recognised.");
    }

    private static byte[] Bytes(JsonObject envelope, string property)
    {
        string text = envelope[property]?.GetValue<string>() ?? throw new FormatException($"The backup is missing '{property}'.");
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"The backup's '{property}' is damaged.", ex);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations = Iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
