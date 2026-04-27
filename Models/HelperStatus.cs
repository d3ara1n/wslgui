namespace WslGui.Models;

public enum HelperState
{
    Unknown,
    NotInstalled,
    Installed,
    Error
}

public sealed record HelperStatus(
    string DistributionName,
    HelperState State,
    DateTimeOffset CheckedAt,
    string? Version,
    string Path,
    string Message);
