namespace WslGui.Models;

public enum HealthCheckKind
{
    ShellCommand,
    TcpPort,
    SystemdService
}

public sealed record HealthCheckDefinition(
    string DistributionName,
    HealthCheckKind Kind,
    string Name,
    string? ShellCommand = null,
    string? Host = null,
    int? Port = null,
    string? ServiceName = null,
    TimeSpan? Timeout = null);
