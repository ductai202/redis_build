using System.Text;
using Hyperion.Core;
using Hyperion.Protocol;
using Xunit;

namespace Hyperion.Core.Tests;

public class CmsCommandsTests
{
    private readonly CommandExecutor _executor;

    public CmsCommandsTests()
    {
        _executor = new CommandExecutor();
    }

    private string ExecuteCommand(string cmd, params string[] args)
    {
        var responseBytes = _executor.Execute(new RespCommand { Cmd = cmd, Args = args });
        return Encoding.UTF8.GetString(responseBytes);
    }

    [Fact]
    public void CmsInitByDim_ShouldInitialize()
    {
        Assert.Equal("+OK\r\n", ExecuteCommand("CMS.INITBYDIM", "mycms", "2000", "5"));
        Assert.Equal("-ERR CMS: key already exists\r\n", ExecuteCommand("CMS.INITBYDIM", "mycms", "2000", "5"));
    }

    [Fact]
    public void CmsInitByProb_ShouldInitialize()
    {
        Assert.Equal("+OK\r\n", ExecuteCommand("CMS.INITBYPROB", "mycms2", "0.01", "0.01"));
        Assert.StartsWith("-ERR", ExecuteCommand("CMS.INITBYPROB", "mycms2", "0.01", "0.01"));
    }

    [Fact]
    public void CmsIncrByAndQuery_ShouldWork()
    {
        ExecuteCommand("CMS.INITBYDIM", "mycms3", "1000", "5");

        var incrRes = ExecuteCommand("CMS.INCRBY", "mycms3", "foo", "5", "bar", "10");
        Assert.StartsWith("*2\r\n", incrRes);
        Assert.Contains(":5\r\n", incrRes);
        Assert.Contains(":10\r\n", incrRes);

        var queryRes = ExecuteCommand("CMS.QUERY", "mycms3", "foo", "bar", "baz");
        Assert.StartsWith("*3\r\n", queryRes);
        Assert.Contains(":5\r\n", queryRes);
        Assert.Contains(":10\r\n", queryRes);
        Assert.Contains(":0\r\n", queryRes);
    }
}
