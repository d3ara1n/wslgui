using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WslGui.Services;

namespace WslGui.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const string PulseMode = "周期心跳";
    private const string AnchorMode = "驻留进程";

    private readonly IWslCli wsl;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> keepAliveTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Process> anchorProcesses = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private WslDistributionRow? selectedDistribution;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "正在初始化...";

    [ObservableProperty]
    private string wslStatusText = "尚未读取 WSL 状态。";

    [ObservableProperty]
    private string lastRefreshText = "从未";

    [ObservableProperty]
    private string selectedKeepAliveMode = PulseMode;

    [ObservableProperty]
    private int intervalSeconds = 60;

    public MainWindowViewModel()
        : this(new WslCli())
    {
    }

    internal MainWindowViewModel(IWslCli wsl)
    {
        this.wsl = wsl;
        Distributions.CollectionChanged += OnDistributionsChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<WslDistributionRow> Distributions { get; } = [];

    public IReadOnlyList<string> KeepAliveModes { get; } = [PulseMode, AnchorMode];

    public int TotalCount => Distributions.Count;

    public int RunningCount => Distributions.Count(static row => row.IsRunning);

    public int StoppedCount => Math.Max(0, TotalCount - RunningCount);

    public int ActiveKeepAliveCount => Distributions.Count(static row => row.IsKeepAliveActive);

    public string SelectedDistributionName => SelectedDistribution?.Name ?? "未选择";

    public void Dispose()
    {
        foreach (var name in keepAliveTokens.Keys.ToArray())
        {
            StopKeepAlive(name);
        }

        Distributions.CollectionChanged -= OnDistributionsChanged;
    }

    [RelayCommand(CanExecute = nameof(CanRunWslCommand))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "正在读取 WSL 状态...";

        try
        {
            var distributions = await wsl.ListDistributionsAsync();
            MergeDistributions(distributions);
            WslStatusText = await wsl.GetStatusTextAsync();
            LastRefreshText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            StatusMessage = distributions.Count == 0
                ? "未找到 WSL 发行版。"
                : $"已读取 {distributions.Count} 个 WSL 发行版。";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            WslStatusText = "无法读取 WSL 状态。请确认 Windows Subsystem for Linux 已安装且 wsl.exe 可用。";
        }
        finally
        {
            IsBusy = false;
            NotifySummaryChanged();
            NotifyCommandStatesChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task StartSelectedAsync()
    {
        if (SelectedDistribution is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"正在启动 {SelectedDistribution.Name}...";

        try
        {
            await wsl.StartDistributionAsync(SelectedDistribution.Name);
            StatusMessage = $"{SelectedDistribution.Name} 已启动。";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStatesChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task StopSelectedAsync()
    {
        if (SelectedDistribution is null)
        {
            return;
        }

        var name = SelectedDistribution.Name;
        IsBusy = true;
        StatusMessage = $"正在停止 {name}...";

        try
        {
            StopKeepAlive(name);
            await wsl.TerminateDistributionAsync(name);
            StatusMessage = $"{name} 已停止。";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStatesChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunWslCommand))]
    private async Task ShutdownAllAsync()
    {
        IsBusy = true;
        StatusMessage = "正在关闭全部 WSL 实例...";

        try
        {
            StopAllKeepAlive();
            await wsl.ShutdownAsync();
            StatusMessage = "全部 WSL 实例已关闭。";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStatesChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartKeepAliveSelected))]
    private void StartKeepAliveSelected()
    {
        if (SelectedDistribution is null)
        {
            return;
        }

        var row = SelectedDistribution;
        StopKeepAlive(row.Name);

        var tokenSource = new CancellationTokenSource();
        keepAliveTokens[row.Name] = tokenSource;

        row.IsKeepAliveActive = true;
        row.KeepAliveMode = SelectedKeepAliveMode;
        row.KeepAliveState = "运行中";
        row.LastError = string.Empty;
        row.LastPingText = "等待首次心跳";

        if (SelectedKeepAliveMode == AnchorMode)
        {
            _ = Task.Run(() => RunAnchorKeepAliveAsync(row.Name, tokenSource.Token));
        }
        else
        {
            _ = Task.Run(() => RunPulseKeepAliveAsync(row.Name, Math.Max(5, IntervalSeconds), tokenSource.Token));
        }

        StatusMessage = $"{row.Name} 已启用 {SelectedKeepAliveMode} 保活。";
        NotifySummaryChanged();
        NotifyCommandStatesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStopKeepAliveSelected))]
    private void StopKeepAliveSelected()
    {
        if (SelectedDistribution is not null)
        {
            StopKeepAlive(SelectedDistribution.Name);
        }
    }

    [RelayCommand]
    private void StopAllKeepAlive()
    {
        foreach (var name in keepAliveTokens.Keys.ToArray())
        {
            StopKeepAlive(name);
        }

        StatusMessage = "已停止全部保活策略。";
    }

    private async Task RunPulseKeepAliveAsync(string name, int interval, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await wsl.SendHeartbeatAsync(name, cancellationToken);
                    await UpdateRowAsync(name, row =>
                    {
                        row.HeartbeatCount++;
                        row.KeepAliveState = "运行中";
                        row.LastPingText = DateTimeOffset.Now.ToString("HH:mm:ss");
                        row.LastError = string.Empty;
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await UpdateRowAsync(name, row =>
                    {
                        row.ErrorCount++;
                        row.KeepAliveState = "异常";
                        row.LastError = ex.Message;
                    });
                }

                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            keepAliveTokens.TryRemove(name, out _);
            await MarkKeepAliveStoppedAsync(name);
        }
    }

    private async Task RunAnchorKeepAliveAsync(string name, CancellationToken cancellationToken)
    {
        Process? process = null;

        try
        {
            process = wsl.StartAnchorProcess(name);
            anchorProcesses[name] = process;

            await UpdateRowAsync(name, row =>
            {
                row.HeartbeatCount++;
                row.KeepAliveState = "驻留中";
                row.LastPingText = DateTimeOffset.Now.ToString("HH:mm:ss");
                row.LastError = string.Empty;
            });

            await process.WaitForExitAsync(cancellationToken);

            if (!cancellationToken.IsCancellationRequested)
            {
                await UpdateRowAsync(name, row =>
                {
                    row.ErrorCount++;
                    row.KeepAliveState = "异常";
                    row.LastError = $"驻留进程已退出，退出码 {process.ExitCode}。";
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await UpdateRowAsync(name, row =>
            {
                row.ErrorCount++;
                row.KeepAliveState = "异常";
                row.LastError = ex.Message;
            });
        }
        finally
        {
            if (process is not null)
            {
                TryKill(process);
                process.Dispose();
            }

            anchorProcesses.TryRemove(name, out _);
            keepAliveTokens.TryRemove(name, out _);
            await MarkKeepAliveStoppedAsync(name);
        }
    }

    private void StopKeepAlive(string name)
    {
        if (keepAliveTokens.TryRemove(name, out var tokenSource))
        {
            tokenSource.Cancel();
            tokenSource.Dispose();
        }

        if (anchorProcesses.TryRemove(name, out var process))
        {
            TryKill(process);
            process.Dispose();
        }

        _ = MarkKeepAliveStoppedAsync(name);
        NotifySummaryChanged();
        NotifyCommandStatesChanged();
    }

    private Task MarkKeepAliveStoppedAsync(string name)
    {
        return UpdateRowAsync(name, row =>
        {
            if (!keepAliveTokens.ContainsKey(name))
            {
                row.IsKeepAliveActive = false;
                row.KeepAliveState = "已停止";
                row.KeepAliveMode = "无";
            }
        });
    }

    private Task UpdateRowAsync(string name, Action<WslDistributionRow> update)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var row = Distributions.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                return;
            }

            update(row);
            NotifySummaryChanged();
            NotifyCommandStatesChanged();
        }).GetTask();
    }

    private void MergeDistributions(IReadOnlyList<Models.WslDistribution> distributions)
    {
        var selectedName = SelectedDistribution?.Name;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var distribution in distributions)
        {
            seen.Add(distribution.Name);

            var row = Distributions.FirstOrDefault(item =>
                item.Name.Equals(distribution.Name, StringComparison.OrdinalIgnoreCase));

            if (row is null)
            {
                row = new WslDistributionRow();
                Distributions.Add(row);
            }

            row.Apply(distribution);
        }

        for (var index = Distributions.Count - 1; index >= 0; index--)
        {
            var row = Distributions[index];
            if (!seen.Contains(row.Name))
            {
                StopKeepAlive(row.Name);
                Distributions.RemoveAt(index);
            }
        }

        SelectedDistribution = Distributions.FirstOrDefault(item =>
            selectedName is not null && item.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
            ?? Distributions.FirstOrDefault();
    }

    private bool CanRunWslCommand()
    {
        return !IsBusy;
    }

    private bool CanUseSelectedDistribution()
    {
        return !IsBusy && SelectedDistribution is not null;
    }

    private bool CanStartKeepAliveSelected()
    {
        return !IsBusy && SelectedDistribution is { IsKeepAliveActive: false };
    }

    private bool CanStopKeepAliveSelected()
    {
        return SelectedDistribution is { IsKeepAliveActive: true };
    }

    private void OnDistributionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifySummaryChanged();
    }

    partial void OnSelectedDistributionChanged(WslDistributionRow? value)
    {
        OnPropertyChanged(nameof(SelectedDistributionName));
        NotifyCommandStatesChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifyCommandStatesChanged();
    }

    partial void OnIntervalSecondsChanged(int value)
    {
        if (value < 5)
        {
            IntervalSeconds = 5;
        }
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(StoppedCount));
        OnPropertyChanged(nameof(ActiveKeepAliveCount));
    }

    private void NotifyCommandStatesChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        StartSelectedCommand.NotifyCanExecuteChanged();
        StopSelectedCommand.NotifyCanExecuteChanged();
        ShutdownAllCommand.NotifyCanExecuteChanged();
        StartKeepAliveSelectedCommand.NotifyCanExecuteChanged();
        StopKeepAliveSelectedCommand.NotifyCanExecuteChanged();
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
