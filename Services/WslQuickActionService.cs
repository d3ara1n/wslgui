using System.Diagnostics;

namespace WslGui.Services;

public sealed class WslQuickActionService : IWslQuickActionService
{
    public string GetUncPath(string distributionName, string? relativePath = null)
    {
        if (string.IsNullOrWhiteSpace(distributionName))
        {
            throw new ArgumentException("发行版名称不能为空。", nameof(distributionName));
        }

        var path = $@"\\wsl$\{distributionName.Trim()}";
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return path;
        }

        return $@"{path}\{relativePath.Trim().TrimStart('\\', '/')}";
    }

    public Task<WslQuickActionResult> OpenWindowsTerminalAsync(
        string distributionName,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new WslQuickActionResult(false, "打开 Windows Terminal 已取消。"));
        }

        if (string.IsNullOrWhiteSpace(distributionName))
        {
            return Task.FromResult(new WslQuickActionResult(false, "发行版名称不能为空。"));
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wt.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-w");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("new-tab");
            startInfo.ArgumentList.Add("--title");
            startInfo.ArgumentList.Add(distributionName);
            startInfo.ArgumentList.Add("wsl.exe");
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(distributionName);

            return Task.FromResult(StartProcess(startInfo, "Windows Terminal 已打开。"));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(new WslQuickActionResult(false, exception.Message));
        }
    }

    public Task<WslQuickActionResult> OpenDistributionPathAsync(
        string distributionName,
        string? relativePath = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new WslQuickActionResult(false, "打开发行版目录已取消。"));
        }

        try
        {
            var uncPath = GetUncPath(distributionName, relativePath);
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add(uncPath);
            return Task.FromResult(StartProcess(startInfo, $"已打开 {uncPath}。"));
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(new WslQuickActionResult(false, exception.Message));
        }
    }

    public async Task<WslQuickActionResult> CopyUncPathAsync(
        string distributionName,
        string? relativePath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var uncPath = GetUncPath(distributionName, relativePath);
            var startInfo = new ProcessStartInfo
            {
                FileName = "clip.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new WslQuickActionResult(false, $"无法启动 clip.exe。UNC: {uncPath}");
            }

            await process.StandardInput.WriteAsync(uncPath.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode == 0
                ? new WslQuickActionResult(true, $"已复制 {uncPath}。")
                : new WslQuickActionResult(false, $"复制失败。UNC: {uncPath}");
        }
        catch (OperationCanceledException)
        {
            return new WslQuickActionResult(false, "复制 UNC 路径已取消。");
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return new WslQuickActionResult(false, exception.Message);
        }
    }

    private static WslQuickActionResult StartProcess(ProcessStartInfo startInfo, string successMessage)
    {
        using var process = Process.Start(startInfo);
        return process is null
            ? new WslQuickActionResult(false, "启动外部程序失败。")
            : new WslQuickActionResult(true, successMessage);
    }
}
