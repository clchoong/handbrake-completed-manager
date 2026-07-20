using HandBrakeCompletedManager.Infrastructure;

namespace HandBrakeCompletedManager.Infrastructure.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondaryInstance_SignalsPrimaryInsteadOfAcquiringOwnership()
    {
        var applicationId = $"HandBrakeCompletedManager.Tests.{Guid.NewGuid():N}";
        var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var primary = new SingleInstanceCoordinator(applicationId, () => activation.TrySetResult());

        using var secondary = new SingleInstanceCoordinator(applicationId, () => { });

        Assert.True(primary.IsPrimaryInstance);
        Assert.False(secondary.IsPrimaryInstance);
        await activation.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Ownership_CanBeAcquiredAfterPrimaryDisposes()
    {
        var applicationId = $"HandBrakeCompletedManager.Tests.{Guid.NewGuid():N}";
        using (var primary = new SingleInstanceCoordinator(applicationId, () => { }))
        {
            Assert.True(primary.IsPrimaryInstance);
        }

        using var replacement = new SingleInstanceCoordinator(applicationId, () => { });
        Assert.True(replacement.IsPrimaryInstance);
    }
}
