namespace WslGui.Models;

public enum WslResourceSnapshotStatus
{
    Unknown,
    Available,
    Error
}

public sealed record WslResourceSnapshot(
    string DistributionName,
    WslResourceSnapshotStatus Status,
    DateTimeOffset CapturedAt,
    TimeSpan? Uptime,
    int? ProcessCount,
    long? MemoryTotalKilobytes,
    long? MemoryAvailableKilobytes,
    long? MemoryUsedKilobytes,
    double? LoadAverage1Minute,
    double? LoadAverage5Minutes,
    double? LoadAverage15Minutes,
    string? Message)
{
    public static WslResourceSnapshot Unknown(string distributionName, string? message = null)
    {
        return new WslResourceSnapshot(
            distributionName,
            WslResourceSnapshotStatus.Unknown,
            DateTimeOffset.Now,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            message);
    }

    public static WslResourceSnapshot Error(string distributionName, string message)
    {
        return new WslResourceSnapshot(
            distributionName,
            WslResourceSnapshotStatus.Error,
            DateTimeOffset.Now,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            message);
    }
}
