using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One device on the Devices &amp; sync page (ADR 22): e.g. "Work PC", "AB 0.1.114 · PC · Google Drive", "1 min ago" with a status dot.</summary>
/// <param name="Kind">"This PC", "PC", "Phone" or "Watch".</param>
/// <param name="MethodsText">The methods it uses, e.g. "sync folder, QR codes".</param>
/// <param name="LastText">When its data last arrived here and how, e.g. "14 min ago · sync folder".</param>
/// <param name="Indented">Shown under the device above it (a phone's linked watch, Task 18).</param>
/// <param name="Age">How long ago its newest data arrived; null for this PC and for a device not heard from yet (the sync light reads it).</param>
/// <param name="AppVersion">The app version the device wrote its file with (this PC's own for its row); empty when not known.</param>
/// <param name="DeviceKey">Its id for the battery settings: the sync device id, "this-pc" for this PC, "companion:" and the name for a phone's linked watch.</param>
public sealed record DeviceRowViewModel(
    string Name, string Kind, string MethodsText, string LastText, DeviceSyncState State, string StateText, bool Indented = false,
    TimeSpan? Age = null, string AppVersion = "", string? DeviceKey = null)
{
    /// <summary>A device's part counts as behind once it's older than its interval plus this margin (as on the phone).</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

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

    public Avalonia.Thickness RowMargin => Indented ? new Avalonia.Thickness(28, 0, 0, 0) : new Avalonia.Thickness(0);

    /// <summary>The kind of device as a Material glyph: a PC, a phone or a watch (the sync data has no tablet kind).</summary>
    public MaterialIconKind IconKind => Kind switch
    {
        "Phone" => MaterialIconKind.Cellphone,
        "Watch" => MaterialIconKind.Watch,
        _ => MaterialIconKind.Laptop,
    };

    /// <summary>The Muted line under the name: "AB 0.1.114 · this PC · sessions and guards" (what the list knows of the device).</summary>
    public string DetailText
    {
        get
        {
            string version = AppVersion.Length > 0 ? $"AB {AppVersion}" : "AB";
            string kind = Kind switch
            {
                "This PC" => "this PC",
                "PC" => "PC",
                "Phone" => "phone",
                _ => "watch",
            };
            return $"{version} · {kind} · {MethodsText}";
        }
    }

    /// <summary>The right-hand time: "Now" for this PC, else how long ago its data came ("1 min ago", "3 h ago"), or why there is none.</summary>
    public string AgoText => State switch
    {
        DeviceSyncState.ThisDevice => "Now",
        DeviceSyncState.Unlinked => StateText,
        _ => Age is TimeSpan age ? DevicesViewModel.Ago(age) : "Not yet",
    };

    /// <summary>The dot reads Sleep (stale) instead of Met (fresh) while the device is behind, unlinked or not heard from yet.</summary>
    public bool IsStale => State is DeviceSyncState.Behind or DeviceSyncState.Waiting or DeviceSyncState.Unlinked;
}
