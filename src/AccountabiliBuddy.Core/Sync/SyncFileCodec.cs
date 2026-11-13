using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Seals and opens sync files (ADR 22). A sealed file is "ABS1", the key's 4-byte group id, a 12-byte random nonce, and
/// then AES-256-GCM over the raw-Deflated UTF-8 JSON of the <see cref="SyncFile"/> (camelCase,
/// <see cref="SyncJson.Options"/>), ending with the 16-byte tag. The phone writes and reads the same bytes
/// (android/shared).
/// </summary>
public static class SyncFileCodec
{
    private const int MagicBytes = 4;
    private const int GroupIdBytes = 4;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int HeaderBytes = MagicBytes + GroupIdBytes + NonceBytes;

    // Only a device holding the key can make a file that opens; this keeps a broken one from filling memory.
    private const int MaxJsonBytes = 16 * 1024 * 1024;

    // A device's category list is short; these bound what a file can make a picker show.
    private const int MaxCategories = 100;
    private const int MaxCategoryNameLength = 500;

    private static readonly byte[] Magic = "ABS1"u8.ToArray();

    public static byte[] Seal(SyncFile file, SyncKey key)
    {
        byte[] compressed = Compress(file);
        byte[] output = new byte[HeaderBytes + compressed.Length + TagBytes];
        Magic.CopyTo(output, 0);
        Convert.FromHexString(key.GroupId).CopyTo(output, MagicBytes);
        Span<byte> nonce = output.AsSpan(MagicBytes + GroupIdBytes, NonceBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key.Key, TagBytes);
        aes.Encrypt(nonce, compressed, output.AsSpan(HeaderBytes, compressed.Length), output.AsSpan(HeaderBytes + compressed.Length, TagBytes));
        return output;
    }

    /// <returns>How many bytes <see cref="Seal"/> gives for the file (its nonce is random, its length isn't), without the key.</returns>
    public static int SealedLength(SyncFile file)
    {
        return HeaderBytes + Compress(file).Length + TagBytes;
    }

    /// <summary>A sync file ("ABS1") whose header names another group than <paramref name="key"/>'s: a leftover (from before a device removal), not a problem.</summary>
    public static bool IsOtherGroups(ReadOnlySpan<byte> sealedBytes, SyncKey key)
    {
        return sealedBytes.Length >= MagicBytes + GroupIdBytes
            && sealedBytes[..MagicBytes].SequenceEqual(Magic)
            && !sealedBytes.Slice(MagicBytes, GroupIdBytes).SequenceEqual(Convert.FromHexString(key.GroupId));
    }

    /// <returns>
    /// The file, or null when it isn't one this group's devices wrote or can't be read: another group or key, a
    /// changed or cut-off file, a header other than "ABS1", JSON that isn't a complete sync file, or a format version
    /// newer than this app's.
    /// </returns>
    public static SyncFile? Open(ReadOnlySpan<byte> sealedBytes, SyncKey key)
    {
        if (sealedBytes.Length < HeaderBytes + TagBytes
            || !sealedBytes[..MagicBytes].SequenceEqual(Magic)
            || !sealedBytes.Slice(MagicBytes, GroupIdBytes).SequenceEqual(Convert.FromHexString(key.GroupId)))
        {
            return null;
        }

        ReadOnlySpan<byte> cipherText = sealedBytes[HeaderBytes..^TagBytes];
        byte[] compressed = new byte[cipherText.Length];
        try
        {
            using var aes = new AesGcm(key.Key, TagBytes);
            aes.Decrypt(sealedBytes.Slice(MagicBytes + GroupIdBytes, NonceBytes), cipherText, sealedBytes[^TagBytes..], compressed);
        }
        catch (CryptographicException)
        {
            // The tag doesn't match: another key, or the bytes were changed.
            return null;
        }

        byte[]? json = Inflate(compressed);
        if (json is null)
        {
            return null;
        }

        SyncFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SyncFile>(json, SyncJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }

        return file is not null && IsComplete(file) ? WithCleanGoalParts(WithCleanTaskParts(WithCleanCategories(file))) : null;
    }

    /// <summary>
    /// The device's category list (a file from a newer or odd writer is not trusted): an entry with no name, or one far longer than a
    /// category name can be, is dropped; a colour that is not "#RRGGBB" becomes the palette's first; at most <see cref="MaxCategories"/> are kept.
    /// </summary>
    private static SyncFile WithCleanCategories(SyncFile file)
    {
        if (file.Categories is null)
        {
            return file;
        }

        List<SyncCategory> clean = file.Categories
            .Where(category => category is { Name: not null } && category.Name.Trim().Length is > 0 and <= MaxCategoryNameLength)
            .Select(category => new SyncCategory(category.Name.Trim(), IsHexColor(category.Color) ? category.Color : TaskCategory.Palette[0], category.ColorEditedAt))
            .Take(MaxCategories)
            .ToList();
        return file with { Categories = clean };
    }

    /// <summary>
    /// ADR 76's optional parts, with every entry that lacks what the merge needs left out (no id, no task, no title on a live task,
    /// no name, no view settings, no snapshot); a label colour that can't be read becomes the set's first, and its rings on is cleaned. Leaving an entry out
    /// never deletes it: the merge keeps what it has. The account's Tasks settings keep only stamped, readable fields.
    /// </summary>
    // A label's rings on as the account's (TaskAccountSettingsMerge.Clean): no empty or repeated ids, in id order; over
    // TaskRingDevices.MaxDevices ids drops the list and its stamp (the merge keeps what the receiver has).
    private static SyncTaskLabel CleanRingOn(SyncTaskLabel item)
    {
        if (item.Label.RingOn is null)
        {
            return item;
        }

        IReadOnlyList<Guid>? ringOn = TaskRingDevices.Normalized(item.Label.RingOn);
        return ringOn!.Count > TaskRingDevices.MaxDevices
            ? item with { Label = item.Label with { RingOn = null, RingOnStamp = null } }
            : item with { Label = item.Label with { RingOn = ringOn } };
    }

    private static SyncFile WithCleanTaskParts(SyncFile file)
    {
        return file with
        {
            Tasks = file.Tasks?
                .Where(item => item is { Task: not null } && IsUsableTask(item.Task))
                .Select(item => item.Stamps is null ? item with { Stamps = TaskFieldStamps.None } : item)
                .ToList(),
            Labels = file.Labels?
                .Where(item => item is { Label: { Name: not null } } && item.Label.Id != Guid.Empty)
                .Select(item => TaskLabelColors.IsValid(item.Label.Color) ? item : item with { Label = item.Label with { Color = TaskLabelColors.Set[0] } })
                .Select(CleanRingOn)
                .ToList(),
            Views = file.Views?
                .Where(item => item is { View: { Name: not null, Config: { Only: not null } } } && item.View.Id != Guid.Empty)
                .ToList(),
            TaskLog = file.TaskLog?
                .Where(item => item is { Entry: { Task: not null } } && item.Entry.Id != Guid.Empty && IsUsableTask(item.Entry.Task))
                .ToList(),
            TaskAccountSettings = TaskAccountSettingsMerge.Clean(file.TaskAccountSettings),
        };
    }

    /// <summary>ADR 79's optional goal parts, with every item the merge can't use left out (<see cref="GoalSyncClean"/>); a missing part stays missing.</summary>
    private static SyncFile WithCleanGoalParts(SyncFile file)
    {
        return file with
        {
            GoalAreas = GoalSyncClean.Areas(file.GoalAreas),
            Goals = GoalSyncClean.Goals(file.Goals),
            GoalEntries = GoalSyncClean.Entries(file.GoalEntries),
            GoalReminders = GoalSyncClean.Reminders(file.GoalReminders),
            GoalReviews = GoalSyncClean.Reviews(file.GoalReviews),
        };
    }

    private static bool IsUsableTask(TaskItem task)
    {
        return task.Id != Guid.Empty && (task.DeletedAt is not null || task.Title is not null);
    }

    private static bool IsHexColor(string? color)
    {
        return color is { Length: 7 } && color[0] == '#' && color[1..].All(char.IsAsciiHexDigit);
    }

    /// <summary>
    /// Missing lists and fields come through as null from the records' constructors; a file needs them all, so later
    /// steps never meet a null. Unknown values (a kind, a method or a document key from a newer app) are kept.
    /// </summary>
    private static bool IsComplete(SyncFile file)
    {
        if (file.FormatVersion < 1 || file.FormatVersion > SyncFile.CurrentFormatVersion)
        {
            return false;
        }

        SyncDeviceCard? device = file.Device;
        return device is { Name: not null, Kind: not null, AppVersion: not null, Methods: not null, Seen: not null }
            && device.DeviceId != Guid.Empty
            && device.Methods.All(method => method is not null)
            && file.Sessions is not null
            && file.Sessions.All(session => session is not null)
            && file.Drift is not null
            && file.Drift.All(drift => drift is { App: not null })
            && (file.DayPlan is null or { PlanJson: not null, Version: not null })
            && file.Documents is not null
            && file.Documents.All(document => document is { Key: not null, Json: not null });
    }

    private static byte[] Compress(SyncFile file)
    {
        return Deflate(JsonSerializer.SerializeToUtf8Bytes(file, SyncJson.Options));
    }

    private static byte[] Deflate(byte[] json)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
        {
            deflate.Write(json);
        }

        return output.ToArray();
    }

    /// <returns>The inflated bytes, or null when they aren't raw Deflate data or exceed <see cref="MaxJsonBytes"/>.</returns>
    private static byte[]? Inflate(byte[] compressed)
    {
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxJsonBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
