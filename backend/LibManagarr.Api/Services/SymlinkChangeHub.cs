using System.Threading.Channels;

namespace LibManagarr.Api.Services;

public sealed record SymlinkChange(string Generation, long Revision, string[] Areas);

/// <summary>In-memory invalidations, never a durable event log. A slow reader receives a full resync.</summary>
public sealed class SymlinkChangeHub
{
    private readonly Lock gate = new();
    private readonly HashSet<Channel<SymlinkChange>> clients = [];
    public string Generation { get; } = Guid.NewGuid().ToString("N");
    private long revision;
    public long Revision { get { lock (gate) return revision; } }
    public static readonly string[] All = ["links", "suggestions", "rejections", "count", "settings", "inspect"];

    public void Publish(params string[] areas)
    {
        lock (gate)
        {
            SymlinkChange change = new(Generation, ++revision, areas);
            foreach (Channel<SymlinkChange> client in clients)
                if (!client.Writer.TryWrite(change))
                {
                    while (client.Reader.TryRead(out _)) { /* Replace queued partial invalidations with one full resync. */ }
                    client.Writer.TryWrite(new(Generation, revision, All));
                }
        }
    }

    public Subscription Subscribe()
    {
        lock (gate)
        {
            Channel<SymlinkChange> channel = Channel.CreateBounded<SymlinkChange>(new BoundedChannelOptions(1)
            { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
            clients.Add(channel);
            channel.Writer.TryWrite(new(Generation, revision, All));
            return new(channel.Reader, () => { lock (gate) { clients.Remove(channel); channel.Writer.TryComplete(); } });
        }
    }

    public sealed class Subscription(ChannelReader<SymlinkChange> reader, Action close) : IDisposable
    {
        public ChannelReader<SymlinkChange> Reader { get; } = reader;
        public void Dispose() => close();
    }
}
