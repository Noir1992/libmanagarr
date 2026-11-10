using System.Threading.Channels;

namespace LibManagarr.Api.Services;

/// <summary>
/// The one place a matching run is asked for. A pass looks at every instance and rewrites nothing while
/// the data stands still, so a request does not have to carry which instance moved — and requests must
/// not pile up, because one queued pass already covers whatever triggered it.
/// </summary>
public sealed class SymlinkMatchingRequests
{
    private readonly Channel<byte> signal = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void Request() => signal.Writer.TryWrite(0);

    public async Task WaitForRequestAsync(CancellationToken cancellationToken)
    {
        _ = await signal.Reader.ReadAsync(cancellationToken);
    }
}
