using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hyperion.Core;
using Hyperion.Protocol;
using Microsoft.Extensions.Logging;

namespace Hyperion.Server;

/// <summary>
/// True single-threaded RESP server: one dedicated OS thread (pthread on Linux) owns
/// all command execution. Connections are handled asynchronously (many can be open
/// simultaneously), but every command is executed sequentially on the single event-loop
/// thread — exactly like Redis's single-threaded model and Nietzsche's goroutine loop.
///
/// Key design:
/// - NO locks, NO SemaphoreSlim, NO contention of any kind.
/// - All connections write commands into ONE unbounded Channel.
/// - ONE LongRunning Task drains the channel and executes commands synchronously.
/// - Responses are delivered back to the originating connection via TaskCompletionSource.
/// </summary>
public sealed class SingleThreadServer
{
    private readonly ICommandExecutor _executor;
    private readonly ILogger<SingleThreadServer> _logger;
    private readonly int _port;
    private TcpListener? _listener;

    // Single shared work queue. SingleReader=true allows the Channel to skip
    // concurrency overhead on the consumer side entirely.
    private readonly Channel<WorkItem> _workChannel = Channel.CreateUnbounded<WorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public SingleThreadServer(ICommandExecutor executor, ILogger<SingleThreadServer> logger, int port)
    {
        _executor = executor;
        _logger = logger;
        _port = port;

        // Start the single event-loop thread immediately.
        // LongRunning => dedicated OS thread (pthread on Linux), not a ThreadPool worker.
        // This avoids ThreadPool scheduling jitter and mimics Go's runtime.LockOSThread().
        Task.Factory.StartNew(RunEventLoopAsync, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _logger.LogInformation("Hyperion (single-thread) listening on :{Port}", _port);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Each connection runs its own async read loop.
                // It will write command WorkItems to the shared channel and await responses.
                _ = Task.Run(() => HandleConnectionAsync(client, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            _workChannel.Writer.Complete();
            _listener.Stop();
            _logger.LogInformation("Hyperion server stopped.");
        }
    }

    /// <summary>
    /// The single event loop — runs on its own dedicated OS thread.
    /// Drains WorkItems from the channel, executes them synchronously, and signals completion.
    /// </summary>
    private async Task RunEventLoopAsync()
    {
        await foreach (var item in _workChannel.Reader.ReadAllAsync())
        {
            try
            {
                byte[] response = _executor.Execute(item.Command);
                item.Completion.TrySetResult(response);
            }
            catch (Exception ex)
            {
                item.Completion.TrySetException(ex);
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogDebug("Client connected: {Endpoint}", endpoint);

        using (client)
        {
            var stream = client.GetStream();
            var reader = PipeReader.Create(stream);

            try
            {
                await ProcessClientAsync(reader, stream, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("Client {Endpoint} disconnected: {Reason}", endpoint, ex.Message);
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }

        _logger.LogDebug("Client disconnected: {Endpoint}", endpoint);
    }

    private async Task ProcessClientAsync(PipeReader reader, NetworkStream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await reader.ReadAsync(cancellationToken);
            var buffer = result.Buffer;

            try
            {
                while (TryParseCommand(ref buffer, out var command))
                {
                    if (command is not null)
                    {
                        // Create a work item with RunContinuationsAsynchronously so the
                        // event-loop thread is never hijacked by the continuation.
                        var item = new WorkItem(command);

                        // Enqueue to the single event-loop thread — never blocks.
                        await _workChannel.Writer.WriteAsync(item, cancellationToken);

                        // Suspend this connection handler until the event loop responds.
                        byte[] response = await item.Completion.Task;
                        await stream.WriteAsync(response, cancellationToken);
                    }
                }

                if (result.IsCompleted)
                    break;
            }
            finally
            {
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

    /// <summary>
    /// A single unit of work enqueued by a connection handler and consumed by the event loop.
    /// </summary>
    private sealed class WorkItem
    {
        public RespCommand Command { get; }
        public TaskCompletionSource<byte[]> Completion { get; }

        public WorkItem(RespCommand command)
        {
            Command = command;
            Completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
