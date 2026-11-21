namespace LibManagarr.Api.Services;

public sealed class SymlinkDeletionRefreshRequests
{
    private readonly SymlinkSetupRequests signal = new();
    public void Request() => signal.Request();
    public Task WaitAsync(CancellationToken token) => signal.WaitForRequestAsync(token);
    public Task WaitOrDelayAsync(TimeSpan delay, TimeProvider clock, CancellationToken token) =>
        signal.WaitForRequestOrDelayAsync(delay, clock, token);
}
