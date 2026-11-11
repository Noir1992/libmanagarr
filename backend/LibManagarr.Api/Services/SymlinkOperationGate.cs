namespace LibManagarr.Api.Services;

/// <summary>Coordinates the journal, filesystem and Sonarr steps within this process. Optimistic
/// revisions alone cannot undo an external write after another operation wins the database update.</summary>
public static class SymlinkOperationGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        await Gate.WaitAsync(token);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        public void Dispose() => Gate.Release();
    }
}
