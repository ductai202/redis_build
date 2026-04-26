using System.Buffers;
using System.Text;
using Xunit;

namespace Hyperion.Protocol.Tests;

public class RespParserTests
{
    private static SequenceReader<byte> CreateReader(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var sequence = new ReadOnlySequence<byte>(bytes);
        return new SequenceReader<byte>(sequence);
    }

    [Fact]
    public void TestParseCmd()
    {
        var cases = new Dictionary<string, RespCommand>
        {
            {
                "*3\r\n$3\r\nput\r\n$5\r\nhello\r\n$5\r\nworld\r\n",
                new RespCommand { Cmd = "PUT", Args = new string[] { "hello", "world" } }
            }
        };

        foreach (var (input, expected) in cases)
        {
            var reader = CreateReader(input);
            bool success = RespParser.TryParseCommand(ref reader, out var cmd);
            
            Assert.True(success);
            Assert.NotNull(cmd);
            Assert.Equal(expected.Cmd, cmd!.Cmd);
            Assert.Equal(expected.Args.Length, cmd.Args.Length);
            
            for (int i = 0; i < cmd.Args.Length; i++)
            {
                Assert.Equal(expected.Args[i], cmd.Args[i]);
            }
        }
    }
}
