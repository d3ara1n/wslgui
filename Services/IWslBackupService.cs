namespace WslGui.Services;

public enum WslBackupOperationStatus
{
    Succeeded,
    Failed,
    Cancelled
}

public sealed record WslBackupOperationResult(
    WslBackupOperationStatus Status,
    string Message,
    int? ExitCode = null);

public interface IWslBackupService
{
    Task<WslBackupOperationResult> ExportDistributionAsync(
        string distributionName,
        string outputTarPath,
        CancellationToken cancellationToken = default);

    Task<WslBackupOperationResult> ImportDistributionAsync(
        string distributionName,
        string installLocation,
        string tarPath,
        int? version = null,
        CancellationToken cancellationToken = default);

    Task<WslBackupOperationResult> DangerouslyUnregisterDistributionAsync(
        string distributionName,
        CancellationToken cancellationToken = default);
}
