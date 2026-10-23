using System.Runtime.CompilerServices;

namespace LibManagarr.Api.Services;

/// <summary>Coordinates complete read-modify-write operations for every settings writer.</summary>
internal static class SettingsUpdateCoordinator
{
    private static readonly ConditionalWeakTable<ISettingsService, SemaphoreSlim> Locks = new();

    public static async Task<IDisposable> EnterAsync(ISettingsService service, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = Locks.GetValue(service, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? ownedGate = gate;
        public void Dispose() => Interlocked.Exchange(ref ownedGate, null)?.Release();
    }
}
