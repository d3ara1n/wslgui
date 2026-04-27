using WslGui.Models;

namespace WslGui.Services;

public interface IWslDiagnosticsService
{
    Task<WslResourceSnapshot> GetResourceSnapshotAsync(
        string distributionName,
        CancellationToken cancellationToken = default);

    Task<HealthCheckResult> RunHealthCheckAsync(
        HealthCheckDefinition definition,
        CancellationToken cancellationToken = default);
}
