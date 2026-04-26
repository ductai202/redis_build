namespace Hyperion.Config;

public static class ServerConfig
{
    public static string Protocol { get; set; } = "tcp";
    public static string Port { get; set; } = ":3000";
    public static int MaxConnection { get; set; } = 20000;
    public static int MaxKeyNumber { get; set; } = 1000000;
    public static double EvictionRatio { get; set; } = 0.1;
    public static string EvictionPolicy { get; set; } = "allkeys-lru";
    public static int EpoolMaxSize { get; set; } = 16;
    public static int EpoolLruSampleSize { get; set; } = 5;
    public static int ListenerNumber { get; set; } = 2;
}
