namespace AccountabiliBuddy.App.Services.Remote;

/// <summary>
/// Another device's noise player for the volume popover's "Other devices" rows (ADR 92). <paramref name="Playing"/> is the way it
/// was last asked to go while <paramref name="Sending"/>, otherwise what its file says; <paramref name="SeenAt"/> is when that file was
/// written; <paramref name="Note"/> a refusal or "didn't answer", shown for a few seconds.
/// </summary>
public sealed record NoiseDeviceRow(Guid Id, string Name, bool IsPc, bool Playing, string? Title, bool Locked, bool Sending, DateTimeOffset SeenAt, string? Note);
