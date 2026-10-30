namespace AccountabiliBuddy.Core.Sync;

/// <summary>A device's reading of a rekey (ADR 54): <see cref="NewCode"/> is set only for <see cref="RekeyReadingKind.NewKey"/>.</summary>
public sealed record RekeyReading(RekeyReadingKind Kind, string? NewCode);
