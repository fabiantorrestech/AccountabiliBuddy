namespace AccountabiliBuddy.Core.Domain;

/// <summary>A block that comes back on at its own time that day instead of its device's (schema v11, ADR 37).</summary>
public sealed record BlockReenable(BlockRef Block, TimeOnly At);
