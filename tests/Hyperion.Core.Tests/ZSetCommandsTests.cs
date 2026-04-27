using System.Text;
using Hyperion.Core;
using Hyperion.Protocol;
using Xunit;

namespace Hyperion.Core.Tests;

public class ZSetCommandsTests
{
    private readonly CommandExecutor _executor;
    
    public ZSetCommandsTests()
    {
        _executor = new CommandExecutor();
    }

    private string ExecuteCommand(string cmd, params string[] args)
    {
        var responseBytes = _executor.Execute(new RespCommand { Cmd = cmd, Args = args });
        return Encoding.UTF8.GetString(responseBytes);
    }

    [Fact]
    public void Zadd_ShouldAddMembers()
    {
        Assert.Equal(":2\r\n", ExecuteCommand("ZADD", "myzset", "10.5", "a", "20", "b"));
        Assert.Equal(":0\r\n", ExecuteCommand("ZADD", "myzset", "10.5", "a")); // Already exists
        Assert.Equal(":0\r\n", ExecuteCommand("ZADD", "myzset", "15", "a")); // Score updated, but not added
    }

    [Fact]
    public void Zrem_ShouldRemoveMembers()
    {
        ExecuteCommand("ZADD", "myzset", "10", "a", "20", "b");
        Assert.Equal(":1\r\n", ExecuteCommand("ZREM", "myzset", "a", "c")); // Only 'a' removed
        Assert.Equal(":0\r\n", ExecuteCommand("ZREM", "myzset", "a")); // Already removed
    }

    [Fact]
    public void Zscore_ShouldReturnScore()
    {
        ExecuteCommand("ZADD", "myzset", "10.5", "a");
        Assert.Equal("$4\r\n10.5\r\n", ExecuteCommand("ZSCORE", "myzset", "a"));
        Assert.Equal("$-1\r\n", ExecuteCommand("ZSCORE", "myzset", "b")); // Not found
    }

    [Fact]
    public void Zrank_ShouldReturnRank()
    {
        ExecuteCommand("ZADD", "myzset", "10", "a", "20", "b", "30", "c");
        Assert.Equal(":0\r\n", ExecuteCommand("ZRANK", "myzset", "a"));
        Assert.Equal(":1\r\n", ExecuteCommand("ZRANK", "myzset", "b"));
        Assert.Equal(":2\r\n", ExecuteCommand("ZRANK", "myzset", "c"));
        Assert.Equal("$-1\r\n", ExecuteCommand("ZRANK", "myzset", "d")); // Not found
    }

    [Fact]
    public void Zrange_ShouldReturnMembersInRange()
    {
        ExecuteCommand("ZADD", "myzset", "10", "a", "20", "b", "30", "c");
        var res = ExecuteCommand("ZRANGE", "myzset", "0", "-1");
        
        Assert.StartsWith("*3\r\n", res);
        Assert.Contains("$1\r\na\r\n", res);
        Assert.Contains("$1\r\nb\r\n", res);
        Assert.Contains("$1\r\nc\r\n", res);
    }
}
