using AccountabiliBuddy.Core.Import;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>A blocker extension found in a browser profile, with copies of the data it keeps there.</summary>
/// <param name="Where">The browser and profile, e.g. "Edge" or "Chrome, Work".</param>
/// <param name="DnrRulesJson">The browser's copy of its blocking rules (rules.json), if it has one.</param>
/// <param name="StorageLogs">Its extension storage logs (LevelDB *.log), oldest first.</param>
/// <param name="HasCompactedStorage">Part of its storage is in compacted *.ldb files, which can't be read.</param>
public sealed record BlockerExtensionData(
    ImportSource Source,
    string Where,
    string? DnrRulesJson,
    IReadOnlyList<byte[]> StorageLogs,
    bool HasCompactedStorage);
