using WslGui.Models;

namespace WslGui.Services;

public sealed class WslHelperService : IWslHelperService
{
    public const string CurrentHelperVersion = "1.0.0";
    public const string DefaultHelperPath = "$HOME/.wslgui/helper.sh";

    private readonly IWslCli wslCli;

    public WslHelperService(IWslCli wslCli)
    {
        this.wslCli = wslCli;
    }

    public string HelperPath => "~/.wslgui/helper.sh";

    public async Task<HelperStatus> GetStatusAsync(
        string distributionName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(distributionName))
        {
            return CreateStatus(distributionName, HelperState.Unknown, null, "发行版名称不能为空。");
        }

        const string command = """
            helper="$HOME/.wslgui/helper.sh"
            if [ ! -e "$helper" ]; then
                echo "state=not_installed"
                echo "message=helper 未安装。"
                exit 0
            fi
            if [ ! -x "$helper" ]; then
                echo "state=error"
                echo "message=helper 存在但不可执行。"
                exit 0
            fi
            "$helper" status
            """;

        try
        {
            var result = await wslCli.RunInDistributionAsync(distributionName, command, cancellationToken);
            if (!result.Success)
            {
                return CreateStatus(distributionName, HelperState.Error, null, result.Message);
            }

            return ParseStatus(distributionName, result.StandardOutput);
        }
        catch (OperationCanceledException)
        {
            return CreateStatus(distributionName, HelperState.Unknown, null, "helper 状态检测已取消。");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return CreateStatus(distributionName, HelperState.Error, null, exception.Message);
        }
    }

    public async Task<HelperStatus> InstallOrUpdateAsync(
        string distributionName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(distributionName))
        {
            return CreateStatus(distributionName, HelperState.Unknown, null, "发行版名称不能为空。");
        }

        var command = $$"""
            set -e
            mkdir -p "$HOME/.wslgui"
            cat > "$HOME/.wslgui/helper.sh" <<'WSLGUI_HELPER'
            {{HelperScript}}
            WSLGUI_HELPER
            chmod 700 "$HOME/.wslgui/helper.sh"
            "$HOME/.wslgui/helper.sh" status
            """;

        try
        {
            var result = await wslCli.RunInDistributionAsync(distributionName, command, cancellationToken);
            if (!result.Success)
            {
                return CreateStatus(distributionName, HelperState.Error, null, result.Message);
            }

            return ParseStatus(distributionName, result.StandardOutput);
        }
        catch (OperationCanceledException)
        {
            return CreateStatus(distributionName, HelperState.Unknown, null, "helper 安装/更新已取消。");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return CreateStatus(distributionName, HelperState.Error, null, exception.Message);
        }
    }

    private const string HelperScript = """
        #!/bin/sh
        set -u
        VERSION="1.0.0"

        status() {
            uptime_seconds="$(cut -d. -f1 /proc/uptime 2>/dev/null || printf '')"
            process_count="$(ls -1 /proc 2>/dev/null | grep -E '^[0-9]+$' | wc -l | tr -d ' ')"
            loadavg="$(cat /proc/loadavg 2>/dev/null | awk '{print $1" "$2" "$3}')"
            printf 'state=installed\n'
            printf 'version=%s\n' "$VERSION"
            printf 'uptime_seconds=%s\n' "$uptime_seconds"
            printf 'process_count=%s\n' "$process_count"
            printf 'loadavg=%s\n' "$loadavg"
            printf 'message=ok\n'
        }

        case "${1:-status}" in
            status)
                status
                ;;
            *)
                printf 'state=error\n'
                printf 'version=%s\n' "$VERSION"
                printf 'message=unknown command: %s\n' "$1"
                exit 2
                ;;
        esac
        """;

    private HelperStatus ParseStatus(string distributionName, string output)
    {
        var values = ParseKeyValueOutput(output);
        var state = values.TryGetValue("state", out var stateText)
            ? ParseHelperState(stateText)
            : HelperState.Unknown;

        values.TryGetValue("version", out var version);
        values.TryGetValue("message", out var message);

        return CreateStatus(
            distributionName,
            state,
            string.IsNullOrWhiteSpace(version) ? null : version,
            string.IsNullOrWhiteSpace(message) ? "helper 状态未知。" : message);
    }

    private static HelperStatus CreateStatus(
        string distributionName,
        HelperState state,
        string? version,
        string message)
    {
        return new HelperStatus(
            distributionName,
            state,
            DateTimeOffset.Now,
            version,
            "~/.wslgui/helper.sh",
            message);
    }

    private static HelperState ParseHelperState(string state)
    {
        return state.Trim().ToLowerInvariant() switch
        {
            "installed" => HelperState.Installed,
            "not_installed" => HelperState.NotInstalled,
            "error" => HelperState.Error,
            _ => HelperState.Unknown
        };
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
}
