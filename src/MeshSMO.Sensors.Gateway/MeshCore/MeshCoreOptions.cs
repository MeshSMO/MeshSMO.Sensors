namespace MeshSMO.Sensors.Gateway.MeshCore;

public enum MeshCoreConnectionMode
{
    Disabled,
    Http,
    Serial,
}

public sealed class MeshCoreOptions
{
    public const string SectionName = "MeshCore";

    public MeshCoreConnectionMode Mode { get; set; }

    public int ReconnectDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Collects the repeater panel telemetry (core.*, archive.*, history.*,
    /// ...) into the local outbox. Off by default: the raw panel status is not
    /// consumed by the main API and leaks repeater internals into storage.
    /// </summary>
    public bool TelemetryCollectionEnabled { get; set; }

    public int TelemetryCollectionIntervalSeconds { get; set; } = 60;

    public MeshCoreHttpOptions Http { get; set; } = new();

    public MeshCoreSerialOptions Serial { get; set; } = new();
}

public sealed class MeshCoreHttpOptions
{
    public Uri? BaseAddress { get; set; }

    public string AdminPassword { get; set; } = string.Empty;

    public bool AllowInvalidServerCertificate { get; set; }

    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class MeshCoreSerialOptions
{
    public string PortName { get; set; } = string.Empty;

    public int BaudRate { get; set; } = 115200;

    public int CommandTimeoutSeconds { get; set; } = 10;
}
