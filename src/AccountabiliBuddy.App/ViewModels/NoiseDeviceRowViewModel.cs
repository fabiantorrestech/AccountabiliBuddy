using AccountabiliBuddy.App.Services.Remote;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of the volume popover's "Other devices" (ADR 92): a playing device leads with ▶ and is tinted, a paused one leads with ⏸;
/// a sync mark while a play or pause waits for it; the second line is a refusal, "Last seen", the lock, or the sound's name.
/// </summary>
public sealed class NoiseDeviceRowViewModel
{
    // After this long without a newer file the row is greyed with when the device was last seen.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    public NoiseDeviceRowViewModel(NoiseDeviceRow row, DateTimeOffset now)
    {
        Id = row.Id;
        Name = row.Name;
        Playing = row.Playing;
        Sending = row.Sending;
        IsStale = now - row.SeenAt > StaleAfter;
        HasNote = row.Note is not null;
        Detail = row.Note
            ?? (IsStale ? "Last seen " + row.SeenAt.ToLocalTime().ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) : null)
            ?? (row.Locked && !row.Playing ? "Headphones not connected" : null)
            ?? row.Title
            ?? (row.Playing ? "Playing" : "Paused");
    }

    public Guid Id { get; }

    public string Name { get; }

    public bool Playing { get; }

    public bool Sending { get; }

    public bool IsStale { get; }

    public bool HasNote { get; }

    public string Detail { get; }

    public MaterialIconKind Icon => Playing ? MaterialIconKind.Play : MaterialIconKind.Pause;

    public double RowOpacity => IsStale ? 0.55 : 1.0;

    public string Tip => Playing ? $"Pause noise on {Name}" : $"Play noise on {Name}";
}
