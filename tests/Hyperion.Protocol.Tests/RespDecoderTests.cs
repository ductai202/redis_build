using System.Buffers;
using System.Text;
using Xunit;

namespace Hyperion.Protocol.Tests;

public class RespDecoderTests
{
    private static SequenceReader<byte> CreateReader(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var sequence = new ReadOnlySequence<byte>(bytes);
        return new SequenceReader<byte>(sequence);
    }

    [Fact]
    public void TestSimpleStringDecode()
    {
        var cases = new Dictionary<string, string>
        {
            { "+OK\r\n", "OK" }
        };

        foreach (var (input, expected) in cases)
        {
            var reader = CreateReader(input);
            bool success = RespDecoder.TryDecodeOne(ref reader, out var result);
            
            Assert.True(success);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void TestErrorDecode()
    {
        var cases = new Dictionary<string, string>
        {
            { "-Error message\r\n", "Error message" }
        };

        foreach (var (input, expected) in cases)
        {
            var reader = CreateReader(input);
            bool success = RespDecoder.TryDecodeOne(ref reader, out var result);
            
            Assert.True(success);
            Assert.IsType<Exception>(result);
            Assert.Equal(expected, ((Exception)result!).Message);
        }
    }

    [Fact]
    public void TestInt64Decode()
    {
        var cases = new Dictionary<string, long>
        {
            { ":0\r\n", 0 },
            { ":1000\r\n", 1000 },
            { ":+1000\r\n", 1000 },
            { ":-1000\r\n", -1000 }
        };

        foreach (var (input, expected) in cases)
        {
            var reader = CreateReader(input);
            bool success = RespDecoder.TryDecodeOne(ref reader, out var result);
            
            Assert.True(success);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void TestBulkStringDecode()
    {
        var cases = new Dictionary<string, string?>
        {
            { "$5\r\nhello\r\n", "hello" },
            { "$0\r\n\r\n", "" },
            { "$-1\r\n", null }
        };

        foreach (var (input, expected) in cases)
        {
            var reader = CreateReader(input);
            bool success = RespDecoder.TryDecodeOne(ref reader, out var result);
            
            Assert.True(success);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void TestArrayDecode()
    {
        var cases = new Dictionary<string, object?[]>
        {
            { "*0\r\n", Array.Empty<object>() },
            { "*2\r\n$5\r\nhello\r\n$5\r\nworld\r\n", new object[] { "hello", "world" } },
            { "*3\r\n:1\r\n:2\r\n:3\r\n", new object[] { 1L, 2L, 3L } },
            { "*5\r\n:1\r\n:2\r\n:3\r\n:4\r\n$5\r\nhello\r\n", new object[] { 1L, 2L, 3L, 4L, "hello" } },
            { "*2\r\n*3\r\n:1\r\n:2\r\n:3\r\n*2\r\n+Hello\r\n-World\r\n", new object[] { 
                new object[] { 1L, 2L, 3L }, 
                new object[] { "Hello", new Exception("World") } 
            }}
        };

        foreach (var (input, expected) in cases)
        {
            var reader = CreateReader(input);
            bool success = RespDecoder.TryDecodeOne(ref reader, out var result);
            
            Assert.True(success);
            Assert.IsType<object[]>(result);
            AssertDeepEqual(expected, result);
        }
    }

    private void AssertDeepEqual(object? expected, object? actual)
    {
        if (expected is Exception ex1 && actual is Exception ex2)
        {
            Assert.Equal(ex1.Message, ex2.Message);
        }
        else if (expected is object[] expArr && actual is object[] actArr)
        {
            Assert.Equal(expArr.Length, actArr.Length);
            for (int i = 0; i < expArr.Length; i++)
            {
                AssertDeepEqual(expArr[i], actArr[i]);
            }
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }
}
