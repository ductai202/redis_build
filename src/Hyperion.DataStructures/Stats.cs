namespace Hyperion.DataStructures;

/// <summary>
/// Tracks global statistics for the Redis server.
/// </summary>
public static class Stats
{
    public static class HashKeySpaceStat
    {
        public static long Key { get; set; } = 0;
        public static long Expires { get; set; } = 0;
    }
}
