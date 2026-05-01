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
/// True single-threaded RESP server.
/// Key insight for high performance:
/// - ONE dedicated OS thread (LongRunning) owns all command execution — no locks.
/// - Responses are batched: all commands parsed from a single read are executed
///   and written into a PipeWriter buffer, then flushed ONCE per batch.
///   This eliminates the per-response syscall that was the 15k RPS ceiling.
/// </summary>
public sealed class SingleThreadServer
{
    private readonly ICommandExecutor _executor;
    private readonly ILogger<SingleThreadServer> _logger;
    private readonly int _port;
    private TcpListener? _listener;

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
                try { client = await _listener.AcceptTcpClientAsync(cancellationToken); }
                catch (OperationCanceledException) { break; }
                _ = Task.Run(() => HandleConnectionAsync(client, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            _workChannel.Writer.Complete();
            _listener.Stop();
        }
    }

    private async Task RunEventLoopAsync()
    {
        await foreach (var item in _workChannel.Reader.ReadAllAsync())
        {
            try
            {
                byte[] response = _executor.Execute(item.Command);
                item.Completion.TrySetResult(response);
            }
            catch (Exception ex) { item.Completion.TrySetException(ex); }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = PipeReader.Create(stream);
            // PipeWriter buffers writes and flushes in batches — eliminates per-response syscall
            var writer = PipeWriter.Create(stream);

            try { await ProcessClientAsync(reader, writer, cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("Client disconnected: {Reason}", ex.Message);
            }
            finally
            {
                await reader.CompleteAsync();
                await writer.CompleteAsync();
            }
        }
    }

    private async Task ProcessClientAsync(PipeReader reader, PipeWriter writer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await reader.ReadAsync(cancellationToken);
            var buffer = result.Buffer;
            bool wroteAny = false;

            try
            {
                // Process ALL complete commands from this read in one batch
                while (TryParseCommand(ref buffer, out var command))
                {
                    if (command is not null)
                    {
                        var item = new WorkItem(command);
                        await _workChannel.Writer.WriteAsync(item, cancellationToken);
                        byte[] response = await item.Completion.Task;

                        // Write into PipeWriter buffer — no syscall yet
                        writer.Write(response);
                        wroteAny = true;
                    }
                }

                // Single flush for the entire batch — ONE syscall for all responses
                if (wroteAny)
                {
                    var flushResult = await writer.FlushAsync(cancellationToken);
                    if (flushResult.IsCompleted) break;
                }

                if (result.IsCompleted) break;
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
