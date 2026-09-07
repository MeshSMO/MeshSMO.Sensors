namespace MeshSMO.Sensors.Gateway.MeshCore;

public enum MeshCoreConnectionMode
{
    Disabled,
    Http,
    Serial,

    /// <summary>
    /// Polls sensor nodes through a stock MeshCore companion radio (companion
    /// frame protocol over TCP; Wi-Fi builds listen on port 5000) instead of
    /// the MeshCoreTel repeater panel API.
    /// </summary>
    Companion,
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

    public MeshCoreCompanionOptions Companion { get; set; } = new();
}

public sealed class MeshCoreCompanionOptions
{
    /// <summary>Host name or address of the companion radio's TCP server.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>TCP port of the companion radio; stock Wi-Fi builds listen on 5000.</summary>
    public int Port { get; set; } = 5000;

    public int ConnectTimeoutSeconds { get; set; } = 5;
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
