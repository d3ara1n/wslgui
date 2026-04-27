using WslGui.Models;

namespace WslGui.Services;

public interface IWslHelperService
{
    string HelperPath { get; }

    Task<HelperStatus> GetStatusAsync(
        string distributionName,
        CancellationToken cancellationToken = default);

    Task<HelperStatus> InstallOrUpdateAsync(
        string distributionName,
        CancellationToken cancellationToken = default);
}
