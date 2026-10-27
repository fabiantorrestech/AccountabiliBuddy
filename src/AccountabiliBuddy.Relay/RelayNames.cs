namespace AccountabiliBuddy.Relay;

/// <summary>
/// The strict shapes of the two path parts the relay turns into disk paths. Checked before anything touches the disk,
/// so nothing else (dots, slashes, upper case, other lengths) can ever become part of a path.
/// </summary>
internal static class RelayNames
{
    public const int GroupIdLength = 32;
    public const int FileNameHexLength = 12;
    public const string FileExtension = ".absync";

    /// <summary>The 32 lower-case hex characters of the relay group id (16 bytes).</summary>
    public static bool IsGroupId(string? value)
    {
        return value is { Length: GroupIdLength } && IsLowerHex(value);
    }

    /// <summary>12 lower-case hex characters plus ".absync", the sealed sync file's name.</summary>
    public static bool IsFileName(string? value)
    {
        return value is { Length: FileNameHexLength + 7 }
            && value.EndsWith(FileExtension, StringComparison.Ordinal)
            && IsLowerHex(value.AsSpan(0, FileNameHexLength));
    }

    private static bool IsLowerHex(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            bool digit = c is >= '0' and <= '9';
            bool lower = c is >= 'a' and <= 'f';
            if (!digit && !lower)
            {
                return false;
            }
        }

        return true;
    }
}
