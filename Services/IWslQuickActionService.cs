namespace WslGui.Services;

public sealed record WslQuickActionResult(
    bool Success,
    string Message);

public interface IWslQuickActionService
{
    string GetUncPath(string distributionName, string? relativePath = null);

    Task<WslQuickActionResult> OpenWindowsTerminalAsync(
        string distributionName,
        CancellationToken cancellationToken = default);

    Task<WslQuickActionResult> OpenDistributionPathAsync(
        string distributionName,
        string? relativePath = null,
        CancellationToken cancellationToken = default);

    Task<WslQuickActionResult> CopyUncPathAsync(
        string distributionName,
        string? relativePath = null,
        CancellationToken cancellationToken = default);
}
