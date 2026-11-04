using Microsoft.Data.Sqlite;

namespace LibManagarr.Api.Services;

/// <summary>A restart rebuilds the entire host after its requests and hosted services have stopped.</summary>
public sealed class ApplicationRestart
{
    private int requested;

    public bool IsRequested => Volatile.Read(ref requested) != 0;

    public void Request() => Interlocked.Exchange(ref requested, 1);
}

public static class ApplicationHostRunner
{
    /// <summary>The callback must dispose its host before returning, releasing all service state.</summary>
    public static async Task<int> RunAsync(Func<ApplicationRestart, Task<int>> runHost)
    {
        while (true)
        {
            ApplicationRestart restart = new();
            int exitCode = await runHost(restart);
            if (exitCode != 0 || !restart.IsRequested)
            {
                return exitCode;
            }

            // Pooled handles must not keep the old library open during restore on Windows.
            SqliteConnection.ClearAllPools();
        }
    }
}
