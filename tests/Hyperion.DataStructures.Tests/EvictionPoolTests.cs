using Hyperion.Config;
using Hyperion.DataStructures;
using Xunit;

namespace Hyperion.DataStructures.Tests;

public class EvictionPoolTests
{
    [Fact]
    public void Push_ShouldMaintainSortedOrderAndMaxSize()
    {
        var pool = new EvictionPool();

        for (int i = 0; i < 20; i++)
        {
            pool.Push($"key{i}", 1000 - i);
        }

        Assert.Equal(ServerConfig.EpoolMaxSize, pool.Count);

        var oldest = pool.Pop();
        Assert.NotNull(oldest);
        Assert.Equal("key19", oldest.Key);
        Assert.Equal(981, oldest.LastAccessTime);
    }
}
