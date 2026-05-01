using FluentAssertions;
using Hyperion.Core;
using Hyperion.Core.Commands;
using Hyperion.Protocol;
using Xunit;

namespace Hyperion.Core.Tests;

public class ListCommandsTests
{
    private readonly Storage _storage;
    private readonly ListCommands _listCommands;

    public ListCommandsTests()
    {
        _storage = new Storage();
        _listCommands = new ListCommands(_storage);
    }

    [Fact]
    public void LPush_And_LPop_ShouldWork()
    {
        var lpushRes = _listCommands.LPush(new[] { "mylist", "v1", "v2" });
        RespDecoder.Decode(lpushRes, out var decoded, out _);
        decoded.Should().Be(2);

        var lpopRes = _listCommands.LPop(new[] { "mylist" });
        RespDecoder.Decode(lpopRes, out decoded, out _);
        decoded.Should().Be("v2");

        lpopRes = _listCommands.LPop(new[] { "mylist" });
        RespDecoder.Decode(lpopRes, out decoded, out _);
        decoded.Should().Be("v1");
    }

    [Fact]
    public void RPush_And_RPop_ShouldWork()
    {
        var rpushRes = _listCommands.RPush(new[] { "mylist", "v1", "v2" });
        RespDecoder.Decode(rpushRes, out var decoded, out _);
        decoded.Should().Be(2);

        var rpopRes = _listCommands.RPop(new[] { "mylist" });
        RespDecoder.Decode(rpopRes, out decoded, out _);
        decoded.Should().Be("v2");

        rpopRes = _listCommands.RPop(new[] { "mylist" });
        RespDecoder.Decode(rpopRes, out decoded, out _);
        decoded.Should().Be("v1");
    }

    [Fact]
    public void LRange_ShouldReturnElements()
    {
        _listCommands.RPush(new[] { "mylist", "v1", "v2", "v3" });

        var lrangeRes = _listCommands.LRange(new[] { "mylist", "0", "-1" });
        RespDecoder.Decode(lrangeRes, out var decoded, out _);

        var arr = decoded as object[];
        arr.Should().NotBeNull();
        arr!.Length.Should().Be(3);
        arr[0].Should().Be("v1");
        arr[1].Should().Be("v2");
        arr[2].Should().Be("v3");
    }
}
