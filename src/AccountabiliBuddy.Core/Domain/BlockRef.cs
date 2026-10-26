namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// One block on one device (ADR 37): <see cref="Device"/> "phone" or "pc:&lt;device id&gt;", <see cref="Kind"/> one of
/// the constants, the blocker's own <see cref="Id"/> (a Curbox group id, a Cold Turkey block name) and its name.
/// </summary>
public sealed record BlockRef(string Device, string Kind, string Id, string Name)
{
    public const string Phone = "phone";
    public const string CurboxAppGroup = "curbox.appGroup";
    public const string CurboxKeywordGroup = "curbox.keywordGroup";
    public const string CurboxGrayscaleGroup = "curbox.grayscaleGroup";
    public const string CurboxKeywordBlocker = "curbox.keywordBlocker";
    public const string CurboxReelBlocker = "curbox.reelBlocker";
    public const string ColdTurkeyBlock = "coldturkey.block";

    public static string Pc(Guid deviceId) => $"pc:{deviceId}";

    /// <summary>The same block, whatever its name now.</summary>
    public bool Same(BlockRef other) => Device == other.Device && Kind == other.Kind && Id == other.Id;
}
