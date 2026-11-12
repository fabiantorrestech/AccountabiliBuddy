namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// One of the user's devices as the "Rings on" lists show it (Settings › Alerts &amp; on-screen › Task reminders ring on, and each
/// label's Rings on ▾): read from the Devices &amp; sync page's list. A PC can't be turned off: it always rings for its own reminders.
/// </summary>
/// <param name="Id">Its sync device id (this PC's own; empty when this PC has none yet).</param>
/// <param name="Name">"Work PC", "Pixel 8".</param>
/// <param name="IsPc">A PC (this one or another): always rings for its own reminders.</param>
/// <param name="IsThisPc">This computer.</param>
/// <param name="Status">"This PC", "Synced 1 min ago", "Last synced 2 days ago", "Not synced yet".</param>
public sealed record TaskRingDevice(Guid Id, string Name, bool IsPc, bool IsThisPc, string Status);
