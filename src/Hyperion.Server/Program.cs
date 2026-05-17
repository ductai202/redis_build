using System;
using System.Threading;
using System.Threading.Tasks;
using Hyperion.Persistence;
using Hyperion.Server;
using Microsoft.Extensions.Logging;

namespace Hyperion;

class Program
{
    static async Task<int> Main(string[] args)
    {
        int port       = 3000;
        string mode    = "multi";
        int workers    = Environment.ProcessorCount;
        int ioHandlers = Math.Max(1, Environment.ProcessorCount / 2);
        LogLevel minLog = LogLevel.Warning;
        int delayUs    = 0;
        bool noSave    = false;

        // Persistence defaults
        string dbFilename = "dump.rdb";
        string dbDir      = Directory.GetCurrentDirectory();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--port"       && int.TryParse(args[i + 1], out int argPort))   port       = argPort;
            if (args[i] == "--mode")                                                         mode       = args[i + 1].ToLowerInvariant();
            if (args[i] == "--workers"    && int.TryParse(args[i + 1], out int argWorkers)) workers    = argWorkers;
            if (args[i] == "--io"         && int.TryParse(args[i + 1], out int argIo))      ioHandlers = argIo;
            if (args[i] == "--delay-us"   && int.TryParse(args[i + 1], out int argDelay))   delayUs    = argDelay;
            if (args[i] == "--log"        && Enum.TryParse(args[i + 1], true, out LogLevel level)) minLog = level;
            if (args[i] == "--dbfilename")                                                   dbFilename = args[i + 1];
            if (args[i] == "--dir")                                                          dbDir      = args[i + 1];
        }
        if (Array.Exists(args, a => a == "--no-save")) noSave = true;

        var persistenceConfig = noSave
            ? PersistenceConfig.Disabled
            : new PersistenceConfig
            {
                RdbFilePath = Path.Combine(dbDir, dbFilename)
            };

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(minLog);
        });

        var logger = loggerFactory.CreateLogger<Program>();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            logger.LogInformation("Shutdown signal received, stopping...");
            cts.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

        Console.WriteLine($"Starting Hyperion in [{mode}] mode on port {port} " +
                          $"(Workers: {workers}, IO: {ioHandlers}, RDB: {persistenceConfig.RdbFilePath})");

        try
        {
            if (mode == "single")
            {
                var server = new SingleThreadServer(
                    loggerFactory.CreateLogger<SingleThreadServer>(),
                    port,
                    persistenceConfig,
                    delayUs);
                await server.RunAsync(cts.Token);
            }
            else
            {
                var server = new HyperionServer(
                    loggerFactory, port, workers, ioHandlers, delayUs, persistenceConfig);
                await server.RunAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            if (minLog <= LogLevel.Information)
                logger.LogInformation("Server shut down completely.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Server crashed.");
            return 1;
        }

        return 0;
    }
}
