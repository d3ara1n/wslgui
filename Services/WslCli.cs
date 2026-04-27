using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WslGui.Models;

namespace WslGui.Services;

public sealed class WslCli : IWslCli
{
    private static readonly Regex ColumnSeparator = new(@"\s{2,}", RegexOptions.Compiled);

    public async Task<IReadOnlyList<WslDistribution>> ListDistributionsAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["--list", "--verbose"], cancellationToken);
        ThrowIfFailed(result, "读取 WSL 发行版列表失败");
        return ParseDistributions(result.StandardOutput);
    }

    public async Task<string> GetStatusTextAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["--status"], cancellationToken);
        return result.Success
            ? CleanOutput(result.StandardOutput).Trim()
            : result.Message;
    }

    public async Task StartDistributionAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await RunInDistributionAsync(name, "exit 0", cancellationToken);
        ThrowIfFailed(result, $"启动 {name} 失败");
    }

    public async Task TerminateDistributionAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["--terminate", name], cancellationToken);
        ThrowIfFailed(result, $"停止 {name} 失败");
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["--shutdown"], cancellationToken);
        ThrowIfFailed(result, "关闭全部 WSL 实例失败");
    }

    public async Task SendHeartbeatAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await RunInDistributionAsync(name, "printf wslgui-heartbeat >/dev/null", cancellationToken);
        ThrowIfFailed(result, $"{name} 心跳失败");
    }

    public Process StartAnchorProcess(string name)
    {
        var process = new Process
        {
            StartInfo = CreateStartInfo([
                "-d",
                name,
                "--exec",
                "/bin/sh",
                "-lc",
                "trap 'exit 0' TERM INT; while :; do sleep 3600; done"
            ]),
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动 {name} 的驻留保活进程。");
        }

        return process;
    }

    private static Task<WslCommandResult> RunInDistributionAsync(
        string name,
        string command,
        CancellationToken cancellationToken)
    {
        return RunAsync(["-d", name, "--exec", "/bin/sh", "-lc", command], cancellationToken);
    }

    private static async Task<WslCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(arguments)
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new WslCommandResult(process.ExitCode, CleanOutput(stdout), CleanOutput(stderr));
    }

    private static ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Unicode,
            StandardErrorEncoding = Encoding.Unicode,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static IReadOnlyList<WslDistribution> ParseDistributions(string output)
    {
        var rows = new List<WslDistribution>();
        var lines = CleanOutput(output)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var candidate = line.TrimStart();

            if (candidate.StartsWith("NAME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isDefault = candidate.StartsWith('*');
            if (isDefault)
            {
                candidate = candidate[1..].TrimStart();
            }

            var columns = ColumnSeparator
                .Split(candidate)
                .Where(static part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

            if (columns.Length < 3)
            {
                continue;
            }

            var versionText = columns[^1];
            _ = int.TryParse(versionText, out var version);
            rows.Add(new WslDistribution(
                string.Join(' ', columns.Take(columns.Length - 2)).Trim(),
                isDefault,
                columns[^2].Trim(),
                version == 0 ? null : version));
        }

        return rows;
    }

    private static string CleanOutput(string output)
    {
        return output.Replace("\0", string.Empty);
    }

    private static void ThrowIfFailed(WslCommandResult result, string prefix)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException($"{prefix}: {result.Message}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
