using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WslGui.Models;
using WslGui.Services;

namespace WslGui.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const string PulseMode = "周期心跳";
    private const string AnchorMode = "驻留进程";

    private readonly IWslCli wsl;
    private readonly ISettingsStore settingsStore;
    private readonly IAutoStartService autoStartService;
    private readonly IAppLogger logger;
    private readonly IUserNotificationService notifications;
    private readonly IWslDiagnosticsService diagnostics;
    private readonly IWslHelperService helperService;
    private readonly IWslQuickActionService quickActions;
    private readonly IWslBackupService backupService;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> keepAliveTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Process> anchorProcesses = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings settings = AppSettings.CreateDefault();
    private bool isLoadingSettings;

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

    [ObservableProperty]
    private bool startWithWindows;

    [ObservableProperty]
    private bool startMinimizedToTray;

    [ObservableProperty]
    private bool autoRestoreSelectedKeepAlive;

    [ObservableProperty]
    private string lastNotificationText = "无通知";

    [ObservableProperty]
    private string resourceSummary = "尚未采集。";

    [ObservableProperty]
    private string healthCheckKind = "Shell 命令";

    [ObservableProperty]
    private string healthShellCommand = "true";

    [ObservableProperty]
    private string healthTcpHost = "127.0.0.1";

    [ObservableProperty]
    private int healthTcpPort = 22;

    [ObservableProperty]
    private string healthServiceName = "ssh";

    [ObservableProperty]
    private string healthCheckResultText = "尚未检查。";

    [ObservableProperty]
    private string helperStatusText = "尚未检测。";

    [ObservableProperty]
    private string exportTarPath = string.Empty;

    [ObservableProperty]
    private string importName = string.Empty;

    [ObservableProperty]
    private string importInstallLocation = string.Empty;

    [ObservableProperty]
    private string importTarPath = string.Empty;

    [ObservableProperty]
    private string unregisterConfirmation = string.Empty;

    public MainWindowViewModel()
        : this(
            new WslCli(),
            new JsonSettingsStore(),
            new WindowsAutoStartService(),
            new FileAppLogger(),
            new UserNotificationService())
    {
    }

    internal MainWindowViewModel(
        IWslCli wsl,
        ISettingsStore settingsStore,
        IAutoStartService autoStartService,
        IAppLogger logger,
        IUserNotificationService notifications)
        : this(
            wsl,
            settingsStore,
            autoStartService,
            logger,
            notifications,
            new WslDiagnosticsService(wsl),
            new WslHelperService(wsl),
            new WslQuickActionService(),
            new WslBackupService(wsl))
    {
    }

    internal MainWindowViewModel(
        IWslCli wsl,
        ISettingsStore settingsStore,
        IAutoStartService autoStartService,
        IAppLogger logger,
        IUserNotificationService notifications,
        IWslDiagnosticsService diagnostics,
        IWslHelperService helperService,
        IWslQuickActionService quickActions,
        IWslBackupService backupService)
    {
        this.wsl = wsl;
        this.settingsStore = settingsStore;
        this.autoStartService = autoStartService;
        this.logger = logger;
        this.notifications = notifications;
        this.diagnostics = diagnostics;
        this.helperService = helperService;
        this.quickActions = quickActions;
        this.backupService = backupService;
        Distributions.CollectionChanged += OnDistributionsChanged;
        logger.EntryLogged += OnEntryLogged;
        notifications.NotificationRequested += OnNotificationRequested;

        foreach (var entry in logger.RecentEntries)
        {
            LogEntries.Add(entry);
        }

        _ = InitializeAsync();
    }

    public ObservableCollection<WslDistributionRow> Distributions { get; } = [];

    public ObservableCollection<AppLogEntry> LogEntries { get; } = [];

    public IReadOnlyList<string> KeepAliveModes { get; } = [PulseMode, AnchorMode];

    public IReadOnlyList<string> HealthCheckKinds { get; } = ["Shell 命令", "TCP 端口", "systemd 服务"];

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

        logger.EntryLogged -= OnEntryLogged;
        notifications.NotificationRequested -= OnNotificationRequested;
        Distributions.CollectionChanged -= OnDistributionsChanged;
    }

    private async Task InitializeAsync()
    {
        isLoadingSettings = true;
        try
        {
            settings = await settingsStore.LoadAsync();
            StartWithWindows = settings.StartWithWindows || autoStartService.IsEnabled();
            StartMinimizedToTray = settings.StartMinimizedToTray;
            IntervalSeconds = settings.DefaultKeepAliveIntervalSeconds;
            logger.Info("Settings", "配置已加载。", settingsStore.SettingsPath);
        }
        catch (Exception ex)
        {
            logger.Error("Settings", "配置加载失败。", ex);
            notifications.Warn("配置加载失败", ex.Message);
            StatusMessage = ex.Message;
        }
        finally
        {
            isLoadingSettings = false;
        }

        await RefreshAsync();
        RestoreConfiguredKeepAliveProfiles();
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
            logger.Info("WSL", StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            WslStatusText = "无法读取 WSL 状态。请确认 Windows Subsystem for Linux 已安装且 wsl.exe 可用。";
            logger.Error("WSL", "读取 WSL 状态失败。", ex);
            notifications.Error("读取 WSL 状态失败", ex.Message);
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
            logger.Info("WSL", StatusMessage);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.Error("WSL", "启动发行版失败。", ex);
            notifications.Error("启动失败", ex.Message);
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
            logger.Info("WSL", StatusMessage);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.Error("WSL", "停止发行版失败。", ex);
            notifications.Error("停止失败", ex.Message);
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
            logger.Warn("WSL", StatusMessage);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.Error("WSL", "关闭全部 WSL 实例失败。", ex);
            notifications.Error("关闭失败", ex.Message);
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
        StartKeepAlive(row);
        SaveSelectedKeepAliveProfile(row, autoRestore: AutoRestoreSelectedKeepAlive);
        logger.Info("KeepAlive", StatusMessage);
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
        logger.Warn("KeepAlive", StatusMessage);
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task RefreshSelectedDiagnosticsAsync()
    {
        if (SelectedDistribution is null)
        {
            return;
        }

        var name = SelectedDistribution.Name;
        IsBusy = true;
        StatusMessage = $"正在采集 {name} 状态...";

        try
        {
            var snapshot = await diagnostics.GetResourceSnapshotAsync(name);
            ResourceSummary = FormatResourceSnapshot(snapshot);

            var helper = await helperService.GetStatusAsync(name);
            HelperStatusText = FormatHelperStatus(helper);

            StatusMessage = $"{name} 状态采集完成。";
            logger.Info("Diagnostics", StatusMessage, ResourceSummary);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.Error("Diagnostics", "状态采集失败。", ex);
            notifications.Error("状态采集失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStatesChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task RunHealthCheckAsync()
    {
        if (SelectedDistribution is null)
        {
            return;
        }

        var definition = BuildHealthCheckDefinition(SelectedDistribution.Name);
        IsBusy = true;
        StatusMessage = $"正在检查 {definition.Name}...";

        try
        {
            var result = await diagnostics.RunHealthCheckAsync(definition);
            HealthCheckResultText = $"{result.CheckedAt:HH:mm:ss} {result.Status}: {result.Message}";
            StatusMessage = HealthCheckResultText;
            logger.Info("Health", StatusMessage);

            if (result.Status != HealthCheckStatus.Healthy)
            {
                notifications.Warn("健康检查异常", HealthCheckResultText);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.Error("Health", "健康检查失败。", ex);
            notifications.Error("健康检查失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStatesChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task InstallOrUpdateHelperAsync()
    {
        if (SelectedDistribution is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"正在安装 helper 到 {SelectedDistribution.Name}...";

        try
        {
            var status = await helperService.InstallOrUpdateAsync(SelectedDistribution.Name);
            HelperStatusText = FormatHelperStatus(status);
            StatusMessage = HelperStatusText;
            logger.Info("Helper", StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.Error("Helper", "helper 安装失败。", ex);
            notifications.Error("helper 安装失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStatesChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task OpenTerminalAsync()
    {
        if (SelectedDistribution is not null)
        {
            await RunQuickActionAsync(() => quickActions.OpenWindowsTerminalAsync(SelectedDistribution.Name));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task OpenRootDirectoryAsync()
    {
        if (SelectedDistribution is not null)
        {
            await RunQuickActionAsync(() => quickActions.OpenDistributionPathAsync(SelectedDistribution.Name));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task OpenHomeDirectoryAsync()
    {
        if (SelectedDistribution is not null)
        {
            await RunQuickActionAsync(() => quickActions.OpenDistributionPathAsync(SelectedDistribution.Name, "home"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task ShowUncPathAsync()
    {
        if (SelectedDistribution is null)
        {
            return;
        }

        await RunQuickActionAsync(() => quickActions.CopyUncPathAsync(SelectedDistribution.Name));
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task ExportSelectedAsync()
    {
        if (SelectedDistribution is null)
        {
            return;
        }

        var outputPath = string.IsNullOrWhiteSpace(ExportTarPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{SelectedDistribution.Name}.tar")
            : ExportTarPath;

        await RunBackupOperationAsync(() => backupService.ExportDistributionAsync(SelectedDistribution.Name, outputPath));
    }

    [RelayCommand(CanExecute = nameof(CanRunWslCommand))]
    private async Task ImportDistributionAsync()
    {
        await RunBackupOperationAsync(() => backupService.ImportDistributionAsync(
            ImportName,
            ImportInstallLocation,
            ImportTarPath,
            version: 2));
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedDistribution))]
    private async Task UnregisterSelectedAsync()
    {
        if (SelectedDistribution is null)
        {
            return;
        }

        if (!string.Equals(UnregisterConfirmation, SelectedDistribution.Name, StringComparison.Ordinal))
        {
            StatusMessage = "注销前必须准确输入发行版名称。";
            notifications.Warn("危险操作已阻止", StatusMessage);
            return;
        }

        StopKeepAlive(SelectedDistribution.Name);
        await RunBackupOperationAsync(() => backupService.DangerouslyUnregisterDistributionAsync(SelectedDistribution.Name));
        UnregisterConfirmation = string.Empty;
        await RefreshAsync();
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
                    logger.Error("KeepAlive", $"{name} 心跳失败。", ex);
                    notifications.Error($"{name} 保活失败", ex.Message);
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
                logger.Error("KeepAlive", $"{name} 驻留保活进程异常退出。", $"ExitCode={process.ExitCode}");
                notifications.Error($"{name} 保活失败", $"驻留进程已退出，退出码 {process.ExitCode}。");
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
            logger.Error("KeepAlive", $"{name} 驻留保活失败。", ex);
            notifications.Error($"{name} 保活失败", ex.Message);
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
        logger.Info("KeepAlive", $"{name} 保活已停止。");
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

    private void MergeDistributions(IReadOnlyList<WslDistribution> distributions)
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
            ApplyProfileToRow(row);
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
        if (value is not null)
        {
            ApplyProfileToSelection(value);
        }

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
            return;
        }

        if (!isLoadingSettings)
        {
            settings.DefaultKeepAliveIntervalSeconds = value;
            _ = SaveSettingsAsync();
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (isLoadingSettings)
        {
            return;
        }

        try
        {
            autoStartService.SetEnabled(value);
            settings.StartWithWindows = value;
            _ = SaveSettingsAsync();
            logger.Info("Settings", value ? "已启用开机自启动。" : "已关闭开机自启动。");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.Error("Settings", "更新开机自启动失败。", ex);
            notifications.Error("自启动设置失败", ex.Message);
        }
    }

    partial void OnStartMinimizedToTrayChanged(bool value)
    {
        if (isLoadingSettings)
        {
            return;
        }

        settings.StartMinimizedToTray = value;
        _ = SaveSettingsAsync();
    }

    partial void OnAutoRestoreSelectedKeepAliveChanged(bool value)
    {
        if (isLoadingSettings || SelectedDistribution is null)
        {
            return;
        }

        SaveSelectedKeepAliveProfile(SelectedDistribution, value);
    }

    private void RestoreConfiguredKeepAliveProfiles()
    {
        foreach (var row in Distributions)
        {
            if (!settings.DistributionProfiles.TryGetValue(row.Name, out var profile) || !profile.AutoRestore)
            {
                continue;
            }

            SelectedKeepAliveMode = profile.Mode;
            IntervalSeconds = profile.IntervalSeconds ?? settings.DefaultKeepAliveIntervalSeconds;
            StartKeepAlive(row);
        }
    }

    private void StartKeepAlive(WslDistributionRow row)
    {
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

    private void ApplyProfileToRow(WslDistributionRow row)
    {
        if (!settings.DistributionProfiles.TryGetValue(row.Name, out var profile))
        {
            return;
        }

        row.KeepAliveMode = row.IsKeepAliveActive ? row.KeepAliveMode : profile.Mode;
    }

    private void ApplyProfileToSelection(WslDistributionRow row)
    {
        isLoadingSettings = true;
        try
        {
            if (settings.DistributionProfiles.TryGetValue(row.Name, out var profile))
            {
                SelectedKeepAliveMode = profile.Mode;
                IntervalSeconds = profile.IntervalSeconds ?? settings.DefaultKeepAliveIntervalSeconds;
                AutoRestoreSelectedKeepAlive = profile.AutoRestore;
            }
            else
            {
                AutoRestoreSelectedKeepAlive = false;
            }
        }
        finally
        {
            isLoadingSettings = false;
        }
    }

    private void SaveSelectedKeepAliveProfile(WslDistributionRow row, bool autoRestore)
    {
        var profile = settings.DistributionProfiles.TryGetValue(row.Name, out var existing)
            ? existing
            : new KeepAliveProfile();

        profile.Mode = SelectedKeepAliveMode;
        profile.IntervalSeconds = Math.Max(5, IntervalSeconds);
        profile.AutoRestore = autoRestore;
        settings.DistributionProfiles[row.Name] = profile;
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            logger.Error("Settings", "保存配置失败。", ex);
            notifications.Error("配置保存失败", ex.Message);
        }
    }

    private void OnEntryLogged(object? sender, AppLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogEntries.Add(entry);
            while (LogEntries.Count > FileAppLogger.MaxRecentEntryCount)
            {
                LogEntries.RemoveAt(0);
            }
        });
    }

    private void OnNotificationRequested(object? sender, UserNotificationRequestedEventArgs e)
    {
        LastNotificationText = $"{e.Notification.Timestamp:HH:mm:ss} {e.Notification.Title}: {e.Notification.Message}";
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
        RefreshSelectedDiagnosticsCommand.NotifyCanExecuteChanged();
        RunHealthCheckCommand.NotifyCanExecuteChanged();
        InstallOrUpdateHelperCommand.NotifyCanExecuteChanged();
        OpenTerminalCommand.NotifyCanExecuteChanged();
        OpenRootDirectoryCommand.NotifyCanExecuteChanged();
        OpenHomeDirectoryCommand.NotifyCanExecuteChanged();
        ShowUncPathCommand.NotifyCanExecuteChanged();
        ExportSelectedCommand.NotifyCanExecuteChanged();
        ImportDistributionCommand.NotifyCanExecuteChanged();
        UnregisterSelectedCommand.NotifyCanExecuteChanged();
    }

    private HealthCheckDefinition BuildHealthCheckDefinition(string name)
    {
        return HealthCheckKind switch
        {
            "TCP 端口" => new HealthCheckDefinition(
                name,
                Models.HealthCheckKind.TcpPort,
                $"TCP {HealthTcpHost}:{HealthTcpPort}",
                Host: HealthTcpHost,
                Port: HealthTcpPort,
                Timeout: TimeSpan.FromSeconds(10)),
            "systemd 服务" => new HealthCheckDefinition(
                name,
                Models.HealthCheckKind.SystemdService,
                HealthServiceName,
                ServiceName: HealthServiceName,
                Timeout: TimeSpan.FromSeconds(10)),
            _ => new HealthCheckDefinition(
                name,
                Models.HealthCheckKind.ShellCommand,
                "Shell 命令",
                ShellCommand: HealthShellCommand,
                Timeout: TimeSpan.FromSeconds(10))
        };
    }

    private async Task RunQuickActionAsync(Func<Task<WslQuickActionResult>> action)
    {
        try
        {
            var result = await action();
            StatusMessage = result.Message;
            if (result.Success)
            {
                logger.Info("QuickAction", result.Message);
            }
            else
            {
                logger.Warn("QuickAction", result.Message);
                notifications.Warn("快速入口失败", result.Message);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.Error("QuickAction", "快速入口执行失败。", ex);
            notifications.Error("快速入口失败", ex.Message);
        }
    }

    private async Task RunBackupOperationAsync(Func<Task<WslBackupOperationResult>> operation)
    {
        IsBusy = true;
        StatusMessage = "正在执行 WSL 长任务...";

        try
        {
            var result = await operation();
            StatusMessage = result.Message;

            if (result.Status == WslBackupOperationStatus.Succeeded)
            {
                logger.Info("Backup", result.Message);
            }
            else
            {
                logger.Warn("Backup", result.Message);
                notifications.Warn("WSL 操作未完成", result.Message);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.Error("Backup", "WSL 长任务失败。", ex);
            notifications.Error("WSL 长任务失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStatesChanged();
        }
    }

    private static string FormatResourceSnapshot(WslResourceSnapshot snapshot)
    {
        if (snapshot.Status != WslResourceSnapshotStatus.Available)
        {
            return $"{snapshot.Status}: {snapshot.Message ?? "不可用"}";
        }

        var uptime = snapshot.Uptime is null ? "-" : $"{(int)snapshot.Uptime.Value.TotalHours}h {snapshot.Uptime.Value.Minutes}m";
        var memory = snapshot.MemoryUsedKilobytes is null || snapshot.MemoryTotalKilobytes is null
            ? "-"
            : $"{snapshot.MemoryUsedKilobytes.Value / 1024} / {snapshot.MemoryTotalKilobytes.Value / 1024} MB";
        var load = snapshot.LoadAverage1Minute is null
            ? "-"
            : $"{snapshot.LoadAverage1Minute:0.00}, {snapshot.LoadAverage5Minutes:0.00}, {snapshot.LoadAverage15Minutes:0.00}";

        return $"uptime {uptime}; 进程 {snapshot.ProcessCount?.ToString() ?? "-"}; 内存 {memory}; load {load}";
    }

    private static string FormatHelperStatus(HelperStatus status)
    {
        var version = string.IsNullOrWhiteSpace(status.Version) ? "-" : status.Version;
        return $"{status.State}; version {version}; {status.Message}";
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
