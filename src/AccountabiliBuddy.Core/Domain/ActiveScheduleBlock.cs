namespace AccountabiliBuddy.Core.Domain;

/// <summary>A schedule block together with the concrete local start/end of its current occurrence.</summary>
public sealed record ActiveScheduleBlock(ScheduleBlock Block, DateTime LocalStart, DateTime LocalEnd);
