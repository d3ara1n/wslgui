using System.Diagnostics;
using WslGui.Models;

namespace WslGui.Services;

public interface IWslCli
{
    Task<WslCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);

    Task<WslCommandResult> RunInDistributionAsync(
        string name,
        string command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WslDistribution>> ListDistributionsAsync(CancellationToken cancellationToken = default);

    Task<string> GetStatusTextAsync(CancellationToken cancellationToken = default);

    Task StartDistributionAsync(string name, CancellationToken cancellationToken = default);

    Task TerminateDistributionAsync(string name, CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);

    Task SendHeartbeatAsync(string name, CancellationToken cancellationToken = default);

    Process StartAnchorProcess(string name);
}
