using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Hyperion.Core;
using Hyperion.Protocol;
using Microsoft.Extensions.Logging;

namespace Hyperion.Server;

/// <summary>
/// Handles network I/O for a subset of connected clients.
/// Reads RESP commands, dispatches them to the global server router,
/// awaits the execution result from a Worker, and writes the response back.
/// </summary>
public class IOHandler
{
    private readonly int _id;
    private readonly HyperionServer _server;
    private readonly ILogger<IOHandler> _logger;

    public IOHandler(int id, HyperionServer server, ILogger<IOHandler> logger)
    {
        _id = id;
        _server = server;
        _logger = logger;
    }

    /// <summary>
    /// Adds a new client connection to be managed by this I/O handler.
    /// Spawns a background task to process the client's stream asynchronously.
    /// </summary>
    public void AddConnection(TcpClient client, CancellationToken cancellationToken)
    {
        _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogDebug("[IOHandler {Id}] Client connected: {Endpoint}", _id, endpoint);

        using (client)
        {
            var stream = client.GetStream();
            var reader = PipeReader.Create(stream);

            try
            {
                await ProcessStreamAsync(reader, stream, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("[IOHandler {Id}] Client {Endpoint} disconnected: {Reason}", _id, endpoint, ex.Message);
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }
    }

    private async Task ProcessStreamAsync(PipeReader reader, NetworkStream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await reader.ReadAsync(cancellationToken);
            var buffer = result.Buffer;

            try
            {
                // Try to parse complete commands from the buffer
                while (TryParseCommand(ref buffer, out var command))
                {
                    if (command is not null)
                    {
                        // 1. Create a task representing this command
                        var task = new WorkerTask(command);

                        // 2. Dispatch to the global server (which will route it to the correct Worker thread)
                        await _server.DispatchAsync(task);

                        // 3. Suspend this async method until the assigned Worker executes the command
                        // This frees up the thread to handle other clients/tasks while waiting.
                        byte[] responseBytes = await task.ReplyCompletion.Task;

                        // 4. Send the result back to the client
                        await stream.WriteAsync(responseBytes, cancellationToken);
                    }
                }

                if (result.IsCompleted)
                    break;
            }
            finally
            {
                // Advance the reader to indicate how much of the buffer was consumed
                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
    }

    private static bool TryParseCommand(ref ReadOnlySequence<byte> buffer, out RespCommand? command)
    {
        var reader = new SequenceReader<byte>(buffer);

        if (RespParser.TryParseCommand(ref reader, out command))
        {
            buffer = buffer.Slice(reader.Position);
            return true;
        }

        return false;
    }
}
