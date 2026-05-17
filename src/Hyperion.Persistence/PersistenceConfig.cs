namespace Hyperion.Persistence;

/// <summary>
/// Configuration for RDB persistence behaviour.
///
/// SAVE POLICIES:
/// A list of (seconds, minChanges) pairs. A snapshot is triggered when any
/// one policy is satisfied: i.e., at least `minChanges` write commands have
/// been executed AND `seconds` have elapsed since the last save.
///
/// Defaults mirror Redis's out-of-the-box configuration:
///   save 3600 1    → save after 1 hour if at least 1 change occurred
///   save 300  100  → save after 5 minutes if at least 100 changes occurred
///   save 60   10000 → save after 1 minute if at least 10,000 changes occurred
/// </summary>
public sealed class PersistenceConfig
{
    /// <summary>Path to the RDB dump file.</summary>
    public string RdbFilePath { get; set; } = RdbConstants.DefaultFileName;

    /// <summary>
    /// List of automatic-save policies.
    /// Each tuple is (afterSeconds, ifAtLeastChanges).
    /// An empty list disables automatic saving (BGSAVE/SAVE still work manually).
    /// </summary>
    public List<(long Seconds, long MinChanges)> SavePolicies { get; set; } =
    [
        (3600, 1),
        (300,  100),
        (60,   10000)
    ];

    /// <summary>When true, a final synchronous SAVE is issued on server shutdown.</summary>
    public bool SaveOnShutdown { get; set; } = true;

    /// <summary>Disables all automatic persistence (save policies + shutdown save).</summary>
    public static PersistenceConfig Disabled => new()
    {
        SavePolicies   = [],
        SaveOnShutdown = false
    };
}
