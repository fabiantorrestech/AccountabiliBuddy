using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Builds the confirmation board (<see cref="SyncBoard"/>) from what this PC knows: the devices whose files it holds, the receipts in
/// those files, its own receiver-local record and its send log. The colours are the pure rule <see cref="SyncReceiptBoard"/> (judged against
/// each receiver's OWN latest file, so a device that simply has not written since is yellow, never red). No transport and no clock of its
/// own: everything comes in as arguments.
/// </summary>
public static class SyncBoardBuilder
{
    /// <summary>The method order on the board.</summary>
    public static readonly IReadOnlyList<string> Order = [SyncReceiptParts.Folder, SyncReceiptParts.Drive, SyncReceiptParts.Relay, SyncReceiptParts.Qr, SyncReceiptParts.File];

    /// <summary>The items shown under a device, in this order (older task lists and phone actions are internal).</summary>
    public static readonly IReadOnlyList<string> ShownParts = ["tasks", "goals", "goalHistory", "sessions", "dayPlan", "settings", "steps", "lunch"];

    /// <param name="now">This PC's trusted time.</param>
    /// <param name="self">This PC's device id.</param>
    /// <param name="ownMethods">The methods this PC has on (its card's list).</param>
    /// <param name="devices">The devices still in the group whose files this PC holds.</param>
    /// <param name="local">The receiver-local record (what arrived by which method).</param>
    /// <param name="sent">The send log.</param>
    /// <param name="interval">The method's real expected write interval (null: used by hand, never quiet).</param>
    /// <param name="sharedOrdinals">The numbering the report and the lists use (every device this PC holds a file of); null numbers just <paramref name="devices"/>.</param>
    public static SyncBoard Build(
        DateTimeOffset now, Guid self, IReadOnlyCollection<string> ownMethods, IReadOnlyList<ReceivedDevice> devices, SyncReceiptsLocal local, SyncSendLog sent,
        Func<string, TimeSpan?> interval, IReadOnlyDictionary<Guid, string>? sharedOrdinals = null)
    {
        IReadOnlyDictionary<Guid, string> ordinals = sharedOrdinals ?? Ordinals(devices.Select(device => device.File.Device));
        var methods = new List<SyncBoardMethod>();
        foreach (string method in Order.Where(ownMethods.Contains))
        {
            var rows = new List<SyncBoardDevice>();
            foreach (ReceivedDevice device in devices.OrderBy(device => ordinals[device.File.Device.DeviceId], StringComparer.Ordinal))
            {
                rows.Add(Row(now, self, method, device, ordinals, local, sent, interval));
            }

            methods.Add(new SyncBoardMethod(method, TitleOf(method), rows));
        }

        return new SyncBoard(methods, sent.Newest);
    }

    /// <summary>"PC 1", "Phone 1", "Watch 1", "Tablet 1": the stable names the log and the export use (kind, then device id order).</summary>
    public static IReadOnlyDictionary<Guid, string> Ordinals(IEnumerable<SyncDeviceCard> cards)
    {
        var result = new Dictionary<Guid, string>();
        foreach (IGrouping<string, SyncDeviceCard> kind in cards.GroupBy(card => card.Kind))
        {
            int number = 1;
            foreach (SyncDeviceCard card in kind.OrderBy(card => card.DeviceId))
            {
                result[card.DeviceId] = $"{KindLabel(kind.Key)} {number++}";
            }
        }

        return result;
    }

    /// <summary>The method's name on screen.</summary>
    public static string TitleOf(string method) => method switch
    {
        SyncReceiptParts.Folder => "Shared folder",
        SyncReceiptParts.Drive => "Google Drive",
        SyncReceiptParts.Relay => "Own relay",
        SyncReceiptParts.Qr => "QR codes",
        _ => "File import",
    };

    private static string KindLabel(string kind) => kind switch
    {
        SyncDeviceCard.KindPc => "PC",
        SyncDeviceCard.KindPhone => "Phone",
        SyncDeviceCard.KindWatch => "Watch",
        _ => "Device",
    };

    private static SyncBoardDevice Row(
        DateTimeOffset now, Guid self, string method, ReceivedDevice device, IReadOnlyDictionary<Guid, string> ordinals, SyncReceiptsLocal local, SyncSendLog sent,
        Func<string, TimeSpan?> interval)
    {
        SyncFile file = device.File;
        SyncDeviceCard card = file.Device;
        SyncReceipt? mine = file.Receipts?.FirstOrDefault(receipt => receipt.From == self);
        SyncReceipt? theirs = local.Senders.FirstOrDefault(row => row.From == card.DeviceId);
        DateTimeOffset? seen = card.Seen.TryGetValue(self, out DateTimeOffset held) ? held : null;
        var facts = new SyncReceiverFacts(card.Kind, Removed: false, card.WrittenAt, file.Receipts is not null, seen, mine, theirs?.ByMethod);
        SyncSentFacts sentFacts = sent.Facts(card.WrittenAt) ?? new SyncSentFacts(DateTimeOffset.MinValue, new Dictionary<string, DateTimeOffset>());
        SyncDot dot = SyncReceiptBoard.Method(method, now, sentFacts, facts, interval(method));
        DateTimeOffset? reachedAny = sentFacts.ReachedAt.Count == 0 ? null : sentFacts.ReachedAt.Values.Min();
        List<SyncBoardPart> parts = ShownParts
            .Select(code => new SyncBoardPart(code, SyncReceiptBoard.Part(code, now, sentFacts, reachedAny, facts)))
            .ToList();
        DateTimeOffset? confirmed = mine?.ByMethod is not null && mine.ByMethod.TryGetValue(method, out DateTimeOffset at) ? at : null;
        // What this PC itself refused from that device's newest accepted file (ADR 86 "refused"): shown under the device, whatever the method.
        List<string> refusedHere = ShownParts
            .Where(code => theirs?.Parts is { } held && held.TryGetValue(code, out SyncPartReceipt? state) && state.State == SyncReceiptParts.Refused)
            .ToList();
        return new SyncBoardDevice(card.DeviceId, card.Name, ordinals[card.DeviceId], card.Kind, card.AppVersion, dot, confirmed, card.WrittenAt, parts, refusedHere);
    }
}
