namespace AccountabiliBuddy.Core.Domain;

/// <summary>When one device's blocks come back on that day (schema v11, ADR 37); devices not listed use the day's re-enable time.</summary>
public sealed record DeviceReenable(string Device, TimeOnly At);
