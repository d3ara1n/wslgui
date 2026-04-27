using System.Globalization;
using WslGui.Models;

namespace WslGui.Services;

public sealed class WslDiagnosticsService : IWslDiagnosticsService
{
    private const int DefaultHealthCheckTimeoutSeconds = 10;
    private readonly IWslCli wslCli;

    public WslDiagnosticsService(IWslCli wslCli)
    {
        this.wslCli = wslCli;
    }

    public async Task<WslResourceSnapshot> GetResourceSnapshotAsync(
        string distributionName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(distributionName))
        {
            return WslResourceSnapshot.Unknown(distributionName, "发行版名称不能为空。");
        }

        const string command = """
            set +e
            uptime_seconds=$(cut -d. -f1 /proc/uptime 2>/dev/null)
            process_count=$(ls -1 /proc 2>/dev/null | grep -E '^[0-9]+$' | wc -l)
            mem_total_kb=$(awk '/^MemTotal:/ {print $2}' /proc/meminfo 2>/dev/null)
            mem_available_kb=$(awk '/^MemAvailable:/ {print $2}' /proc/meminfo 2>/dev/null)
            read load1 load5 load15 _ < /proc/loadavg 2>/dev/null
            printf 'uptime_seconds=%s\n' "$uptime_seconds"
            printf 'process_count=%s\n' "$process_count"
            printf 'mem_total_kb=%s\n' "$mem_total_kb"
            printf 'mem_available_kb=%s\n' "$mem_available_kb"
            printf 'load1=%s\n' "$load1"
            printf 'load5=%s\n' "$load5"
            printf 'load15=%s\n' "$load15"
            """;

        try
        {
            var result = await wslCli.RunInDistributionAsync(distributionName, command, cancellationToken);
            if (!result.Success)
            {
                return WslResourceSnapshot.Error(distributionName, result.Message);
            }

            var values = ParseKeyValueOutput(result.StandardOutput);
            var uptime = TryGetLong(values, "uptime_seconds", out var uptimeSeconds)
                ? TimeSpan.FromSeconds(uptimeSeconds)
                : (TimeSpan?)null;

            _ = TryGetInt(values, "process_count", out var processCount);
            _ = TryGetLong(values, "mem_total_kb", out var totalMemory);
            _ = TryGetLong(values, "mem_available_kb", out var availableMemory);
            var usedMemory = totalMemory > 0 && availableMemory >= 0
                ? Math.Max(0, totalMemory - availableMemory)
                : (long?)null;

            return new WslResourceSnapshot(
                distributionName,
                WslResourceSnapshotStatus.Available,
                DateTimeOffset.Now,
                uptime,
                processCount <= 0 ? null : processCount,
                totalMemory <= 0 ? null : totalMemory,
                availableMemory < 0 ? null : availableMemory,
                usedMemory,
                TryGetDouble(values, "load1", out var load1) ? load1 : null,
                TryGetDouble(values, "load5", out var load5) ? load5 : null,
                TryGetDouble(values, "load15", out var load15) ? load15 : null,
                null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WslResourceSnapshot.Unknown(distributionName, "资源采集超时。");
        }
        catch (OperationCanceledException)
        {
            return WslResourceSnapshot.Unknown(distributionName, "资源采集已取消。");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return WslResourceSnapshot.Error(distributionName, exception.Message);
        }
    }

    public async Task<HealthCheckResult> RunHealthCheckAsync(
        HealthCheckDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateHealthCheck(definition);
        if (validationError is not null)
        {
            return CreateHealthResult(definition, HealthCheckStatus.Unknown, validationError);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(definition.Timeout ?? TimeSpan.FromSeconds(DefaultHealthCheckTimeoutSeconds));

        try
        {
            var command = BuildHealthCheckCommand(definition);
            var result = await wslCli.RunInDistributionAsync(
                definition.DistributionName,
                command,
                timeoutSource.Token);

            return MapHealthCheckResult(definition, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateHealthResult(definition, HealthCheckStatus.Unknown, "健康检查已取消。");
        }
        catch (OperationCanceledException)
        {
            return CreateHealthResult(definition, HealthCheckStatus.Unknown, "健康检查超时。");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return CreateHealthResult(definition, HealthCheckStatus.Unknown, exception.Message);
        }
    }

    private static HealthCheckResult MapHealthCheckResult(
        HealthCheckDefinition definition,
        WslCommandResult result)
    {
        if (result.Success)
        {
            return CreateHealthResult(
                definition,
                HealthCheckStatus.Healthy,
                FirstOutputLine(result) ?? "健康检查通过。",
                result.ExitCode);
        }

        var status = result.ExitCode == 2
            ? HealthCheckStatus.Unknown
            : HealthCheckStatus.Unhealthy;

        return CreateHealthResult(
            definition,
            status,
            FirstOutputLine(result) ?? result.Message,
            result.ExitCode);
    }

    private static string BuildHealthCheckCommand(HealthCheckDefinition definition)
    {
        return definition.Kind switch
        {
            HealthCheckKind.ShellCommand => definition.ShellCommand!,
            HealthCheckKind.TcpPort => BuildTcpPortCommand(definition.Host ?? "127.0.0.1", definition.Port!.Value),
            HealthCheckKind.SystemdService => BuildSystemdServiceCommand(definition.ServiceName!),
            _ => "echo '未知健康检查类型'; exit 2"
        };
    }

    private static string BuildTcpPortCommand(string host, int port)
    {
        return $$"""
            host={{ShellQuote(host)}}
            port={{port.ToString(CultureInfo.InvariantCulture)}}
            seconds=3
            if command -v nc >/dev/null 2>&1; then
                nc -z -w "$seconds" "$host" "$port"
                exit $?
            fi
            if command -v bash >/dev/null 2>&1; then
                if command -v timeout >/dev/null 2>&1; then
                    timeout "$seconds" bash -lc 'cat < /dev/null > "/dev/tcp/$0/$1"' "$host" "$port"
                else
                    bash -lc 'cat < /dev/null > "/dev/tcp/$0/$1"' "$host" "$port"
                fi
                exit $?
            fi
            echo "发行版内缺少 nc 或 bash，无法执行 TCP 检查。"
            exit 2
            """;
    }

    private static string BuildSystemdServiceCommand(string serviceName)
    {
        return $$"""
            service={{ShellQuote(serviceName)}}
            if ! command -v systemctl >/dev/null 2>&1; then
                echo "systemctl 不可用。"
                exit 2
            fi
            state=$(systemctl is-active "$service" 2>&1)
            code=$?
            printf '%s\n' "$state"
            if [ "$code" -eq 0 ]; then
                exit 0
            fi
            if [ "$state" = "inactive" ] || [ "$state" = "failed" ] || [ "$state" = "activating" ] || [ "$state" = "deactivating" ]; then
                exit 1
            fi
            exit 2
            """;
    }

    private static string? ValidateHealthCheck(HealthCheckDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.DistributionName))
        {
            return "发行版名称不能为空。";
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            return "健康检查名称不能为空。";
        }

        return definition.Kind switch
        {
            HealthCheckKind.ShellCommand when string.IsNullOrWhiteSpace(definition.ShellCommand) =>
                "自定义 shell 命令不能为空。",
            HealthCheckKind.TcpPort when definition.Port is null or <= 0 or > 65535 =>
                "TCP 端口必须在 1 到 65535 之间。",
            HealthCheckKind.SystemdService when string.IsNullOrWhiteSpace(definition.ServiceName) =>
                "systemd service 名称不能为空。",
            _ => null
        };
    }

    private static HealthCheckResult CreateHealthResult(
        HealthCheckDefinition definition,
        HealthCheckStatus status,
        string message,
        int? exitCode = null)
    {
        return new HealthCheckResult(
            definition.DistributionName,
            string.IsNullOrWhiteSpace(definition.Name) ? definition.Kind.ToString() : definition.Name,
            definition.Kind,
            status,
            DateTimeOffset.Now,
            string.IsNullOrWhiteSpace(message) ? "无检查输出。" : message.Trim(),
            exitCode);
    }

    private static Dictionary<string, string> ParseKeyValueOutput(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = line.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            values[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        return values;
    }

    private static bool TryGetInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetLong(
        IReadOnlyDictionary<string, string> values,
        string key,
        out long value)
    {
        value = 0;
        return values.TryGetValue(key, out var text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetDouble(
        IReadOnlyDictionary<string, string> values,
        string key,
        out double value)
    {
        value = 0;
        return values.TryGetValue(key, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string ShellQuote(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private static string? FirstOutputLine(WslCommandResult result)
    {
        var output = string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError
            : result.StandardOutput;

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }
}
