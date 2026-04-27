namespace WslGui.Models;

public sealed class KeepAliveProfile
{
    public string Mode { get; set; } = KeepAliveModes.Pulse;

    public int? IntervalSeconds { get; set; }

    public bool AutoRestore { get; set; }

    public HealthCheckProfile HealthCheck { get; set; } = new();
}

public sealed class HealthCheckProfile
{
    public bool Enabled { get; set; }

    public string Type { get; set; } = HealthCheckTypes.None;

    public string Command { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int? Port { get; set; }

    public int TimeoutSeconds { get; set; } = 10;
}

public static class KeepAliveModes
{
    public const string None = "无";

    public const string Pulse = "周期心跳";

    public const string Anchor = "驻留进程";
}

public static class HealthCheckTypes
{
    public const string None = "none";

    public const string Command = "command";

    public const string TcpPort = "tcp-port";
}
