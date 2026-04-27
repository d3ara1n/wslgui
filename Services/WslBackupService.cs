namespace WslGui.Services;

public sealed class WslBackupService : IWslBackupService
{
    private readonly IWslCli wslCli;

    public WslBackupService(IWslCli wslCli)
    {
        this.wslCli = wslCli;
    }

    public async Task<WslBackupOperationResult> ExportDistributionAsync(
        string distributionName,
        string outputTarPath,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRequired(distributionName, "发行版名称")
            ?? ValidateRequired(outputTarPath, "导出文件路径");
        if (validationError is not null)
        {
            return Failed(validationError);
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputTarPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return await RunWslOperationAsync(
                ["--export", distributionName, outputTarPath],
                $"已导出 {distributionName} 到 {outputTarPath}。",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Cancelled("导出已取消。");
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            return Failed(exception.Message);
        }
    }

    public Task<WslBackupOperationResult> ImportDistributionAsync(
        string distributionName,
        string installLocation,
        string tarPath,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRequired(distributionName, "发行版名称")
            ?? ValidateRequired(installLocation, "安装目录")
            ?? ValidateRequired(tarPath, "导入文件路径");
        if (validationError is not null)
        {
            return Task.FromResult(Failed(validationError));
        }

        if (version is not null and not 1 and not 2)
        {
            return Task.FromResult(Failed("WSL 版本只能是 1 或 2。"));
        }

        var arguments = new List<string>
        {
            "--import",
            distributionName,
            installLocation,
            tarPath
        };

        if (version is not null)
        {
            arguments.Add("--version");
            arguments.Add(version.Value.ToString());
        }

        return RunWslOperationAsync(
            arguments,
            $"已导入 {distributionName}。",
            cancellationToken);
    }

    public Task<WslBackupOperationResult> DangerouslyUnregisterDistributionAsync(
        string distributionName,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRequired(distributionName, "发行版名称");
        if (validationError is not null)
        {
            return Task.FromResult(Failed(validationError));
        }

        return RunWslOperationAsync(
            ["--unregister", distributionName],
            $"已注销 {distributionName}。",
            cancellationToken);
    }

    private async Task<WslBackupOperationResult> RunWslOperationAsync(
        IReadOnlyList<string> arguments,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await wslCli.RunAsync(arguments, cancellationToken);
            return result.Success
                ? new WslBackupOperationResult(WslBackupOperationStatus.Succeeded, successMessage, result.ExitCode)
                : new WslBackupOperationResult(WslBackupOperationStatus.Failed, result.Message, result.ExitCode);
        }
        catch (OperationCanceledException)
        {
            return Cancelled("操作已取消。");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return Failed(exception.Message);
        }
    }

    private static string? ValidateRequired(string value, string displayName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? $"{displayName}不能为空。"
            : null;
    }

    private static WslBackupOperationResult Failed(string message)
    {
        return new WslBackupOperationResult(WslBackupOperationStatus.Failed, message);
    }

    private static WslBackupOperationResult Cancelled(string message)
    {
        return new WslBackupOperationResult(WslBackupOperationStatus.Cancelled, message);
    }
}
