using System.Buffers;
using System.Text;
using Xunit;

namespace Hyperion.Protocol.Tests;

public class RespEncoderTests
{
    [Fact]
    public void TestEncodeString2DArray()
    {
        string[][] decode = new string[][]
        {
            new string[] { "hello", "world" },
            new string[] { "1", "2", "3" },
            new string[] { "xyz" }
        };

        byte[] encoded = RespEncoder.Encode(decode, false);
        string encodedStr = Encoding.UTF8.GetString(encoded);

        string expectedStr = "*3\r\n*2\r\n$5\r\nhello\r\n$5\r\nworld\r\n*3\r\n$1\r\n1\r\n$1\r\n2\r\n$1\r\n3\r\n*1\r\n$3\r\nxyz\r\n";
        Assert.Equal(expectedStr, encodedStr);

        var sequence = new ReadOnlySequence<byte>(encoded);
        var reader = new SequenceReader<byte>(sequence);
        bool success = RespDecoder.TryDecodeOne(ref reader, out var decodedAgain);

        Assert.True(success);
        var outerArray = Assert.IsType<object[]>(decodedAgain);
        Assert.Equal(3, outerArray.Length);

        for (int i = 0; i < 3; i++)
        {
            var innerArray = Assert.IsType<object[]>(outerArray[i]);
            Assert.Equal(decode[i].Length, innerArray.Length);
            for (int j = 0; j < decode[i].Length; j++)
                Assert.Equal(decode[i][j], innerArray[j]);
        }
    }
}
