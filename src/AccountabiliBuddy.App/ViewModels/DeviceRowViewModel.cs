using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One device in the Devices tab (ADR 22): e.g. "Work PC · PC · sync folder · 14 min ago", with its state pill.</summary>
/// <param name="Kind">"This PC", "PC", "Phone" or "Watch".</param>
/// <param name="MethodsText">The methods it uses, e.g. "sync folder, QR codes".</param>
/// <param name="LastText">When its data last arrived here and how, e.g. "14 min ago · sync folder".</param>
/// <param name="Indented">Shown under the device above it (a phone's linked watch, Task 18).</param>
public sealed record DeviceRowViewModel(
    string Name, string Kind, string MethodsText, string LastText, DeviceSyncState State, string StateText, bool Indented = false)
{
    /// <summary>A device's part counts as behind once it's older than its interval plus this margin (as on the phone).</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private static readonly IBrush UpToDateBrush = new SolidColorBrush(Color.FromRgb(4, 120, 87));
    private static readonly IBrush BehindBrush = new SolidColorBrush(Color.FromRgb(180, 83, 9));
    private static readonly IBrush WaitingBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
    private static readonly IBrush UnlinkedBrush = new SolidColorBrush(Color.FromRgb(127, 29, 29));
    private static readonly IBrush ThisDeviceBrush = new SolidColorBrush(Color.FromRgb(15, 118, 110));

    /// <summary>From the age of the newest file from a device (null: none yet) and how often it should come.</summary>
    public static DeviceSyncState StateFor(TimeSpan? age, TimeSpan interval)
    {
        return age switch
        {
            null => DeviceSyncState.Waiting,
            TimeSpan old when old > interval + Margin => DeviceSyncState.Behind,
            _ => DeviceSyncState.UpToDate,
        };
    }

    public IBrush PillBrush => State switch
    {
        DeviceSyncState.UpToDate => UpToDateBrush,
        DeviceSyncState.Behind => BehindBrush,
        DeviceSyncState.Waiting => WaitingBrush,
        DeviceSyncState.Unlinked => UnlinkedBrush,
        _ => ThisDeviceBrush,
    };

    public Avalonia.Thickness RowMargin => Indented ? new Avalonia.Thickness(28, 2, 0, 2) : new Avalonia.Thickness(0, 4, 0, 4);

    // Outlines in a 16 × 16 box, the same drawings as the phone's Devices tab (DeviceKind.iconPath).
    private static readonly Geometry PcIcon = Geometry.Parse("M2 3.5h12v8H2z M6 14h4 M8 11.5V14");
    private static readonly Geometry PhoneIcon = Geometry.Parse("M5.5 1.5h5a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1v-11a1 1 0 0 1 1-1z M7 12.5h2");
    private static readonly Geometry WatchIcon = Geometry.Parse("M8 4a4 4 0 1 1 0 8a4 4 0 1 1 0-8z M6 4.6L6.6 1.5h2.8L10 4.6 M6 11.4l0.6 3.1h2.8l0.6-3.1 M8 6.3V8l1 1");

    /// <summary>The small outline left of the row: a PC, a phone or a watch.</summary>
    public Geometry Icon => Kind switch
    {
        "Phone" => PhoneIcon,
        "Watch" => WatchIcon,
        _ => PcIcon,
    };
}
