using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class BoundedAudioBufferBudgetTests
{
    [Fact]
    public void ReservationsAreGloballyBoundedAndDisconnectReleaseReturnsCapacity()
    {
        var budget = new BoundedAudioBufferBudget(10);

        Assert.True(budget.TryReserve("session-a", 6));
        Assert.True(budget.TryReserve("session-b", 4));
        Assert.False(budget.TryReserve("session-c", 1));
        Assert.Equal(10, budget.ReservedBytes);

        budget.Release("session-a");

        Assert.True(budget.TryReserve("session-c", 6));
        Assert.Equal(10, budget.ReservedBytes);
        budget.Release("session-b");
        budget.Release("session-c");
        Assert.Equal(0, budget.ReservedBytes);
    }
}
