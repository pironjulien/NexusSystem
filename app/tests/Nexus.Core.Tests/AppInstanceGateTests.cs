using Nexus.Core;
using Xunit;

namespace Nexus.Core.Tests;

public class AppInstanceGateTests
{
    [Fact]
    public void RepeatedLaunchNotifiesTheFirstInstanceAndExitAllowsANewOne()
    {
        var key = "NexusSystem.Test." + Guid.NewGuid().ToString("N");
        using var notified = new ManualResetEventSlim();
        using (var first = new AppInstanceGate(key))
        {
            Assert.True(first.IsFirst);
            first.Listen(notified.Set);
            using var second = new AppInstanceGate(key);
            Assert.False(second.IsFirst);
            second.RequestActivation();
            Assert.True(notified.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        using var next = new AppInstanceGate(key);
        Assert.True(next.IsFirst);
    }
}
