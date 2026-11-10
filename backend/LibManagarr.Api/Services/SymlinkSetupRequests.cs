using System.Threading.Channels;

namespace LibManagarr.Api.Services;

/// <summary>
/// The one place a setup pass is asked for. A row that owes work is what the pass reads from the
/// register rather than from the request, so a request only says "look again" and never has to describe
/// which entry or which step — and requests must not pile up, because one queued pass already covers
/// whatever triggered them.
/// </summary>
public sealed class SymlinkSetupRequests
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
