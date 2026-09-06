namespace MeshSMO.Sensors.Gateway.Polling;

public sealed class SensorPollingOptions
{
    public const string SectionName = "SensorPolling";

    public bool Enabled { get; set; } = true;

    /// <summary>Timeout passed to the repeater acquisition window.</summary>
    public int RequestTimeoutMs { get; set; } = 8000;

    public int LoginTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Optional upper bound on the poll interval (seconds); useful to shorten
    /// the registry-configured intervals during bring-up. Values below 30 are
    /// ignored.
    /// </summary>
    public int IntervalOverrideSeconds { get; set; } = 0;

    /// <summary>
    /// Node password used for the ANON_REQ bootstrap login when a request
    /// times out (the node has not added this repeater to its ACL yet).
    /// </summary>
    public string LoginPassword { get; set; } = string.Empty;

    public bool LoginOnTimeout { get; set; } = true;
}
