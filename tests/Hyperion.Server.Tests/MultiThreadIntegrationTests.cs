using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hyperion.Server.Tests;

public class MultiThreadIntegrationTests : IDisposable
{
    private readonly HyperionServer _server;
    private readonly CancellationTokenSource _cts;
    private readonly Task _serverTask;
    private readonly int _port;

    public MultiThreadIntegrationTests()
    {
        // Random port
        var tcpListener = new TcpListener(System.Net.IPAddress.Any, 0);
        tcpListener.Start();
        _port = ((System.Net.IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();

        _server = new HyperionServer(NullLoggerFactory.Instance, _port, numWorkers: 4, numIOHandlers: 4);
        _cts = new CancellationTokenSource();
        _serverTask = _server.RunAsync(_cts.Token);
    }

    private string SendCommand(string[] args)
    {
        using var client = new TcpClient("127.0.0.1", _port);
        using var stream = client.GetStream();

        // Encode RESP array manually for simplicity in tests
        var sb = new StringBuilder();
        sb.Append($"*{args.Length}\r\n");
        foreach (var arg in args)
        {
            sb.Append($"${Encoding.UTF8.GetByteCount(arg)}\r\n{arg}\r\n");
        }

        var reqBytes = Encoding.UTF8.GetBytes(sb.ToString());
        stream.Write(reqBytes, 0, reqBytes.Length);

        var resBytes = new byte[1024];
        int bytesRead = stream.Read(resBytes, 0, resBytes.Length);

        return Encoding.UTF8.GetString(resBytes, 0, bytesRead);
    }

    [Fact]
    public void Ping_ShouldReturnPong()
    {
        var result = SendCommand(new[] { "PING" });
        Assert.Equal("+PONG\r\n", result);
    }

    [Fact]
    public async Task ConcurrentClients_ShouldSetAndGetConsistently()
    {
        int numClients = 100;
        int keysPerClient = 100;

        var tasks = new List<Task>();
        var errors = new ConcurrentBag<string>();

        for (int i = 0; i < numClients; i++)
        {
            int clientId = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    using var client = new TcpClient("127.0.0.1", _port);
                    using var stream = client.GetStream();

                    for (int j = 0; j < keysPerClient; j++)
                    {
                        string key = $"key:{clientId}:{j}";
                        string val = $"val:{clientId}:{j}";

                        // SET command
                        var sbSet = new StringBuilder();
                        sbSet.Append($"*3\r\n$3\r\nSET\r\n${key.Length}\r\n{key}\r\n${val.Length}\r\n{val}\r\n");
                        var setBytes = Encoding.UTF8.GetBytes(sbSet.ToString());
                        stream.Write(setBytes, 0, setBytes.Length);

                        var resBytes = new byte[1024];
                        int bytesRead = stream.Read(resBytes, 0, resBytes.Length);
                        string setRes = Encoding.UTF8.GetString(resBytes, 0, bytesRead);
                        
                        if (setRes != "+OK\r\n") errors.Add($"SET failed: {setRes}");

                        // GET command
                        var sbGet = new StringBuilder();
                        sbGet.Append($"*2\r\n$3\r\nGET\r\n${key.Length}\r\n{key}\r\n");
                        var getBytes = Encoding.UTF8.GetBytes(sbGet.ToString());
                        stream.Write(getBytes, 0, getBytes.Length);

                        bytesRead = stream.Read(resBytes, 0, resBytes.Length);
                        string getRes = Encoding.UTF8.GetString(resBytes, 0, bytesRead);
                        
                        string expectedGetRes = $"${val.Length}\r\n{val}\r\n";
                        if (getRes != expectedGetRes) errors.Add($"GET failed: Expected {expectedGetRes}, got {getRes}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Exception in client {clientId}: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.Empty(errors);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _serverTask.Wait(1000); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
