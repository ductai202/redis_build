using FluentAssertions;
using Hyperion.Core;
using Hyperion.Core.Commands;
using Hyperion.Protocol;
using Xunit;

namespace Hyperion.Core.Tests;

public class HashCommandsTests
{
    private readonly Storage _storage;
    private readonly HashCommands _hashCommands;

    public HashCommandsTests()
    {
        _storage = new Storage();
        _hashCommands = new HashCommands(_storage);
    }

    [Fact]
    public void HSet_And_HGet_ShouldWork()
    {
        var hsetRes = _hashCommands.HSet(new[] { "myhash", "field1", "value1" });
        RespDecoder.Decode(hsetRes, out var decoded, out _);
        decoded.Should().Be(1);

        var hgetRes = _hashCommands.HGet(new[] { "myhash", "field1" });
        RespDecoder.Decode(hgetRes, out decoded, out _);
        decoded.Should().Be("value1");

        var hgetNil = _hashCommands.HGet(new[] { "myhash", "field2" });
        RespDecoder.Decode(hgetNil, out decoded, out _);
        decoded.Should().BeNull();
    }

    [Fact]
    public void HDel_ShouldRemoveFields()
    {
        _hashCommands.HSet(new[] { "myhash", "field1", "value1", "field2", "value2" });

        var hdelRes = _hashCommands.HDel(new[] { "myhash", "field1", "field3" });
        RespDecoder.Decode(hdelRes, out var decoded, out _);
        decoded.Should().Be(1);

        var hgetRes = _hashCommands.HGet(new[] { "myhash", "field1" });
        RespDecoder.Decode(hgetRes, out decoded, out _);
        decoded.Should().BeNull();
    }

    [Fact]
    public void HGetAll_ShouldReturnAllFieldsAndValues()
    {
        _hashCommands.HSet(new[] { "myhash", "f1", "v1", "f2", "v2" });

        var res = _hashCommands.HGetAll(new[] { "myhash" });
        RespDecoder.Decode(res, out var decoded, out _);

        var arr = decoded as object[];
        arr.Should().NotBeNull();
        arr!.Length.Should().Be(4);
        arr.Should().Contain("f1");
        arr.Should().Contain("v1");
        arr.Should().Contain("f2");
        arr.Should().Contain("v2");
    }
}
