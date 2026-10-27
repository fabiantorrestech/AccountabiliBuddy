using System.Threading.Channels;

namespace AccountabiliBuddy.Relay;

/// <summary>
/// The in-process push: every open event stream of a group holds a small queue, and a change is copied into each.
/// A slow stream drops its oldest entries instead of holding anything up (a device re-lists after a reconnect anyway).
/// </summary>
internal sealed class ChangeBroadcaster
{
    private const int QueueSize = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, List<Channel<ChangeEvent>>> _streams = new(StringComparer.Ordinal);
    private int _total;

    /// <summary>Opens a stream for the group, or returns null when the per-group or overall limit is reached.</summary>
    public ChangeSubscription? Subscribe(string gid)
    {
        lock (_gate)
        {
            if (_total >= RelayLimits.MaxStreamsTotal)
            {
                return null;
            }

            if (!_streams.TryGetValue(gid, out List<Channel<ChangeEvent>>? group))
            {
                group = new List<Channel<ChangeEvent>>();
                _streams[gid] = group;
            }

            if (group.Count >= RelayLimits.MaxStreamsPerGroup)
            {
                return null;
            }

            var channel = Channel.CreateBounded<ChangeEvent>(new BoundedChannelOptions(QueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
            group.Add(channel);
            _total++;
            return new ChangeSubscription(channel, () => Unsubscribe(gid, channel));
        }
    }

    public void Publish(string gid, ChangeEvent change)
    {
        lock (_gate)
        {
            if (!_streams.TryGetValue(gid, out List<Channel<ChangeEvent>>? group))
            {
                return;
            }

            foreach (Channel<ChangeEvent> channel in group)
            {
                channel.Writer.TryWrite(change);
            }
        }
    }

    private void Unsubscribe(string gid, Channel<ChangeEvent> channel)
    {
        lock (_gate)
        {
            if (_streams.TryGetValue(gid, out List<Channel<ChangeEvent>>? group) && group.Remove(channel))
            {
                _total--;
                if (group.Count == 0)
                {
                    _streams.Remove(gid);
                }
            }
        }
    }
}
