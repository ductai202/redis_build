using System;
using System.Threading;
using System.Threading.Tasks;
using Hyperion.Core;
using Hyperion.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hyperion;

class Program
{
    static async Task<int> Main(string[] args)
    {
        int port = 3000;
        string mode = "multi";

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--port" && int.TryParse(args[i + 1], out int argPort))
                port = argPort;
            if (args[i] == "--mode")
                mode = args[i + 1].ToLowerInvariant();
        }

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
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

        logger.LogInformation("Starting Hyperion in [{Mode}] mode on port {Port}", mode, port);

        try
        {
            if (mode == "single")
            {
                var executor = new CommandExecutor();
                var server = new SingleThreadServer(executor, loggerFactory.CreateLogger<SingleThreadServer>(), port);
                await server.RunAsync(cts.Token);
            }
            else
            {
                var server = new HyperionServer(loggerFactory, port);
                await server.RunAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
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
