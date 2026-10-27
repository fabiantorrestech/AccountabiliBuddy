using System.Threading.Channels;

namespace AccountabiliBuddy.Relay;

/// <summary>One open event stream's queue. Disposing it removes the stream from its group.</summary>
internal sealed class ChangeSubscription : IDisposable
{
    private readonly Action _onDispose;

    public ChangeSubscription(Channel<ChangeEvent> channel, Action onDispose)
    {
        Channel = channel;
        _onDispose = onDispose;
    }

    public Channel<ChangeEvent> Channel { get; }

    public ChannelReader<ChangeEvent> Reader => Channel.Reader;

    public void Dispose()
    {
        _onDispose();
    }
}
