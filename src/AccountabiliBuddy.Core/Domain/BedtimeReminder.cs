namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// One bedtime reminder (ADR 38): <see cref="MinutesBefore"/> bedtime (0 = at bedtime), a notification or an alarm,
/// and the devices that get it: <see cref="Phone"/>, <see cref="Watch"/>, <see cref="AllPcs"/> or "pc:&lt;device id&gt;".
/// </summary>
/// <param name="Sound">An alarm's sound: "bell", "chime" or "rising".</param>
/// <param name="Volume">An alarm's volume, 0 to 100.</param>
public sealed record BedtimeReminder(
    int MinutesBefore, bool Alarm, IReadOnlyList<string> Devices, string Sound = "bell", int Volume = 80, bool Vibrate = true)
{
    public const string Phone = "phone";
    public const string Watch = "watch";
    public const string AllPcs = "pcs";

    /// <summary>A PC's device token.</summary>
    public static string Pc(Guid deviceId) => $"pc:{deviceId}";

    /// <summary>True when this PC (by its sync device id) gets the reminder.</summary>
    public bool ForPc(Guid deviceId) => Devices.Contains(AllPcs) || Devices.Contains(Pc(deviceId));
}
