using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One device in the Devices tab (ADR 22): e.g. "Work PC · PC · sync folder · 14 min ago", with its state pill.</summary>
/// <param name="Kind">"This PC", "PC", "Phone" or "Watch".</param>
/// <param name="MethodsText">The methods it uses, e.g. "network link, QR codes".</param>
/// <param name="LastText">When its data last arrived here and how, e.g. "14 min ago · sync folder".</param>
/// <param name="Indented">Shown under the device above it (a phone's linked watch, Task 18).</param>
/// <param name="UnpairId">A watch also paired with this PC over Wi-Fi: its pairing, which Unpair removes; null otherwise.</param>
public sealed record DeviceRowViewModel(
    string Name, string Kind, string MethodsText, string LastText, DeviceSyncState State, string StateText, bool Indented = false,
    Guid? UnpairId = null)
{
    public bool CanUnpair => UnpairId is not null;

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
}
