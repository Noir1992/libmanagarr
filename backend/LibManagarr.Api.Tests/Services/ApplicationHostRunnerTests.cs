using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class ApplicationHostRunnerTests
{
    [Fact]
    public async Task RequestedRestart_RebuildsTheHostAfterItsPreviousStateWasDisposed()
    {
        bool disposed = false;
        int starts = 0;
        ApplicationRestart? previous = null;

        int result = await ApplicationHostRunner.RunAsync(restart =>
        {
            starts++;
            if (starts == 1)
            {
                restart.Request();
                previous = restart;
                disposed = true;
            }
            else
            {
                Assert.True(disposed);
                Assert.NotSame(previous, restart);
                Assert.False(restart.IsRequested);
            }
            return Task.FromResult(0);
        });

        Assert.Equal(0, result);
        Assert.Equal(2, starts);
    }

    [Fact]
    public async Task OrdinaryShutdown_DoesNotRestart()
    {
        int starts = 0;
        int result = await ApplicationHostRunner.RunAsync(_ =>
        {
            starts++;
            return Task.FromResult(0);
        });

        Assert.Equal(0, result);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task FailedStart_DoesNotEnterARestartLoop()
    {
        int starts = 0;
        int result = await ApplicationHostRunner.RunAsync(restart =>
        {
            starts++;
            restart.Request();
            return Task.FromResult(1);
        });

        Assert.Equal(1, result);
        Assert.Equal(1, starts);
    }
}
