using System.Diagnostics;
using WslGui.Models;

namespace WslGui.Services;

public interface IWslCli
{
    Task<IReadOnlyList<WslDistribution>> ListDistributionsAsync(CancellationToken cancellationToken = default);

    Task<string> GetStatusTextAsync(CancellationToken cancellationToken = default);

    Task StartDistributionAsync(string name, CancellationToken cancellationToken = default);

    Task TerminateDistributionAsync(string name, CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);

    Task SendHeartbeatAsync(string name, CancellationToken cancellationToken = default);

    Process StartAnchorProcess(string name);
}
