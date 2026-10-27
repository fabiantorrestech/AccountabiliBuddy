using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Start the noise player when focus headphones connect, pause it when they go (app-owned document "headphones", this
/// device only, never synced).
/// </summary>
/// <param name="Wired">Any wired headphones or headset. Off by default.</param>
/// <param name="Bluetooth">The chosen Bluetooth devices. Off by default.</param>
/// <param name="Devices">The Bluetooth devices chosen; with none, Bluetooth does nothing.</param>
/// <param name="OnlyAtTimes">"Only at certain times": auto-play only when a checked time option matches. Off = any time.</param>
/// <param name="DuringPlanWindow">Option: inside a part of one of <paramref name="PlanCategoryIds"/> in today's day plan.</param>
/// <param name="PlanCategoryIds">The plan categories that count; null = the plan's Work category (first Watch category).</param>
/// <param name="OnDaysAndHours">Option: on <paramref name="Days"/> between <paramref name="Start"/> and <paramref name="End"/>.</param>
/// <param name="Days">Null = Monday to Friday.</param>
/// <param name="Start">Null = 8:00 AM.</param>
/// <param name="End">Null = 6:00 PM.</param>
public sealed record HeadphoneSettings(
    bool Wired,
    bool Bluetooth,
    IReadOnlyList<BluetoothAudioDevice> Devices,
    bool OnlyAtTimes = false,
    bool DuringPlanWindow = false,
    IReadOnlyList<Guid>? PlanCategoryIds = null,
    bool OnDaysAndHours = false,
    IReadOnlyList<DayOfWeek>? Days = null,
    TimeOnly? Start = null,
    TimeOnly? End = null)
{
    public static HeadphoneSettings Default { get; } = new(false, false, []);

    public static IReadOnlyList<DayOfWeek> DefaultDays { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static TimeOnly DefaultStart { get; } = new(8, 0);

    public static TimeOnly DefaultEnd { get; } = new(18, 0);

    public bool IsOn => Wired || (Bluetooth && Devices.Count > 0);

    public IReadOnlyList<DayOfWeek> EffectiveDays => Days ?? DefaultDays;

    public TimeOnly EffectiveStart => Start ?? DefaultStart;

    public TimeOnly EffectiveEnd => End ?? DefaultEnd;
}
