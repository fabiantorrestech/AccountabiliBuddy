using System.Text.Json;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services.Remote;

/// <summary>
/// The other devices' noise players, as the volume popover's "Other devices" rows show them, and this PC's play and pause
/// commands for them (ADR 92). A command is a <see cref="SyncAction.KindNoise"/> action in this PC's own file
/// (<see cref="Pending"/>), kept until that device lists it in its acks; its answer, a refusal or "didn't answer in 10 minutes"
/// shows on the device's row for a few seconds. While one waits the sync poll stays fast (<see cref="WantsFastPoll"/>).
/// In memory only: a command is worth applying for 2 minutes at most. UI thread only.
/// </summary>
public sealed class NoiseCommandSender
{
    /// <summary>The first PC version that answers play and pause (ADR 92).</summary>
    public const string PcPlayPauseVersion = "0.1.147";

    /// <summary>The first phone version that writes its noise player and answers noise commands (ADR 92).</summary>
    public const string PhonePlayPauseVersion = "0.1.82";

    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NoteShownFor = TimeSpan.FromSeconds(8);

    private readonly ReceivedDevicesService _received;
    private readonly SyncIdentityService _identity;
    private readonly TimeProvider _time;
    private readonly List<SyncAction> _pending = [];
    private readonly Dictionary<Guid, (string Text, DateTimeOffset Until)> _notes = [];
    private readonly DispatcherTimer _tick;

    public NoiseCommandSender(ReceivedDevicesService received, SyncIdentityService identity, TimeProvider time)
    {
        _received = received;
        _identity = identity;
        _time = time;
        _received.Changed += (_, _) => Dispatcher.UIThread.Post(Check);
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _tick.Tick += (_, _) => Check();
    }

    /// <summary>Raised on the UI thread when the rows or <see cref="Pending"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when <see cref="Pending"/> changed, so this PC's file is written again.</summary>
    public event EventHandler? PendingChanged;

    /// <summary>This PC's commands still waiting for their device, for its file's "actions".</summary>
    public IReadOnlyList<SyncAction> Pending => _pending.ToList();

    /// <summary>True while a command waits, so the sync poll stays at its fast rate for the answer.</summary>
    public bool WantsFastPoll => _pending.Count > 0;

    /// <summary>
    /// Every other device whose file carries a noise player, by name. A device with a play or pause still waiting shows the way it
    /// was asked to go, with <see cref="NoiseDeviceRow.Sending"/> set until it answers.
    /// </summary>
    public IReadOnlyList<NoiseDeviceRow> Others()
    {
        Guid self = _identity.DeviceId;
        DateTimeOffset now = _time.GetUtcNow();
        return _received.Active
            .Where(device => device.File.Device.DeviceId != self && device.File.Noise is not null)
            .OrderBy(device => device.File.Device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.File.Device.DeviceId)
            .Select(device =>
            {
                SyncDeviceCard card = device.File.Device;
                SyncNoise noise = device.File.Noise!;
                List<SyncAction> waiting = _pending.Where(action => action.Target == card.DeviceId).ToList();
                bool? asked = waiting.Select(OpOf).LastOrDefault(op => op is SyncNoiseRequest.OpPlay or SyncNoiseRequest.OpPause) is string op
                    ? op == SyncNoiseRequest.OpPlay
                    : null;
                string? note = _notes.TryGetValue(card.DeviceId, out var shown) && shown.Until > now ? shown.Text : null;
                return new NoiseDeviceRow(card.DeviceId, card.Name, card.Kind == SyncDeviceCard.KindPc, asked ?? noise.Playing, noise.Name, noise.Locked,
                    waiting.Count > 0, card.WrittenAt, note);
            })
            .ToList();
    }

    /// <summary>Asks the device <paramref name="deviceId"/> to play or pause its noise, replacing a play or pause for it still waiting.</summary>
    public void SetPlaying(Guid deviceId, bool play)
    {
        ReceivedDevice? device = _received.Active.FirstOrDefault(d => d.File.Device.DeviceId == deviceId && d.File.Noise is not null);
        if (device is null)
        {
            return;
        }

        if (!SupportsPlayPause(device.File.Device))
        {
            Say(deviceId, $"Update AccountabiliBuddy on {device.File.Device.Name}");
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _notes.Remove(deviceId);
        _pending.RemoveAll(action => action.Target == deviceId && OpOf(action) is SyncNoiseRequest.OpPlay or SyncNoiseRequest.OpPause);
        string text = JsonSerializer.Serialize(new { op = play ? SyncNoiseRequest.OpPlay : SyncNoiseRequest.OpPause });
        _pending.Add(new SyncAction(Guid.NewGuid(), SyncAction.KindNoise, deviceId, null, text, _time.GetUtcNow()));
        _tick.Start();
        Raise(pendingChanged: true);
    }

    /// <summary>True when the device's app answers play and pause (by the version on its card).</summary>
    public static bool SupportsPlayPause(SyncDeviceCard card) =>
        CompareVersions(card.AppVersion, card.Kind == SyncDeviceCard.KindPc ? PcPlayPauseVersion : PhonePlayPauseVersion) >= 0;

    /// <summary>Compares dotted versions number by number ("0.1.107" &gt; "0.1.99"); a part that isn't a number counts as 0.</summary>
    public static int CompareVersions(string a, string b)
    {
        static int[] Parts(string version) => version.Split('+', '-')[0].Split('.').Select(part => int.TryParse(part, out int n) ? n : 0).ToArray();
        int[] left = Parts(a);
        int[] right = Parts(b);
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            int difference = (i < left.Length ? left[i] : 0) - (i < right.Length ? right[i] : 0);
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }

    // Answered commands leave (a refusal shows on the row); ones no device answered in 10 minutes are given up on.
    private void Check()
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool changed = false;
        foreach (SyncAction action in _pending.ToList())
        {
            ReceivedDevice? device = _received.Active.FirstOrDefault(d => d.File.Device.DeviceId == action.Target);
            string name = device?.File.Device.Name ?? "That device";
            if (device?.File.Acks?.Contains(action.Id) == true)
            {
                _pending.Remove(action);
                changed = true;
                SyncActionResult? result = device.File.ActionResults?.FirstOrDefault(r => r.Id == action.Id);
                if (result is null || result.Status == SyncActionStatus.Unsupported)
                {
                    Say(action.Target, $"Update AccountabiliBuddy on {name}");
                }
                else if (!result.Ok)
                {
                    Say(action.Target, result.Message ?? $"{name} didn't do that");
                }
            }
            else if (now - action.At > GiveUpAfter)
            {
                _pending.Remove(action);
                changed = true;
                Say(action.Target, $"{name} didn't answer. Is it on and syncing?");
            }
        }

        List<Guid> expired = _notes.Where(pair => pair.Value.Until <= now).Select(pair => pair.Key).ToList();
        expired.ForEach(id => _notes.Remove(id));
        bool notesGone = expired.Count > 0;
        if (_pending.Count == 0 && _notes.Count == 0)
        {
            _tick.Stop();
        }

        if (changed || notesGone)
        {
            Raise(pendingChanged: changed);
        }
        else
        {
            // A newer file may have changed what a row shows.
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Say(Guid deviceId, string text)
    {
        _notes[deviceId] = (text, _time.GetUtcNow() + NoteShownFor);
        _tick.Start();
    }

    private void Raise(bool pendingChanged)
    {
        if (pendingChanged)
        {
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string? OpOf(SyncAction action)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(action.Text ?? "{}");
            return document.RootElement.TryGetProperty("op", out JsonElement op) ? op.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

