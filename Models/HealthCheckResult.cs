namespace WslGui.Models;

public enum HealthCheckStatus
{
    Unknown,
    Healthy,
    Unhealthy
}

public sealed record HealthCheckResult(
    string DistributionName,
    string Name,
    HealthCheckKind Kind,
    HealthCheckStatus Status,
    DateTimeOffset CheckedAt,
    string Message,
    int? ExitCode = null);
