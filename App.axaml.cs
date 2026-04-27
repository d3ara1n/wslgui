using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using WslGui.Services;
using WslGui.ViewModels;
using WslGui.Views;

namespace WslGui;

public partial class App : Application
{
    private TrayIcon? trayIcon;
    private NativeMenuItem? trayStatusItem;
    private MainWindow? mainWindow;
    private MainWindowViewModel? mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var wsl = new WslCli();
            var settingsStore = new JsonSettingsStore();
            var autoStart = new WindowsAutoStartService();
            var logger = new FileAppLogger();
            var notifications = new UserNotificationService();
            var settings = LoadStartupSettings(settingsStore, logger);
            var startHidden = Environment.GetCommandLineArgs()
                .Any(static arg => arg.Equals("--tray", StringComparison.OrdinalIgnoreCase))
                || settings.StartMinimizedToTray;

            mainViewModel = new MainWindowViewModel(
                wsl,
                settingsStore,
                autoStart,
                logger,
                notifications,
                new WslDiagnosticsService(wsl),
                new WslHelperService(wsl),
                new WslQuickActionService(),
                new WslBackupService(wsl));

            mainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            if (startHidden)
            {
                mainWindow.Opened += (_, _) => mainWindow.Hide();
            }

            mainViewModel.PropertyChanged += (_, _) => UpdateTrayStatus();
            notifications.NotificationRequested += (_, _) => UpdateTrayStatus();

            desktop.MainWindow = mainWindow;
            trayIcon = CreateTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private TrayIcon CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("打开主界面");
        showItem.Click += (_, _) => ShowMainWindow();

        var hideItem = new NativeMenuItem("隐藏主界面");
        hideItem.Click += (_, _) => mainWindow?.Hide();

        trayStatusItem = new NativeMenuItem("状态初始化中")
        {
            IsEnabled = false
        };

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => ExitApplication(desktop);

        menu.Items.Add(trayStatusItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(showItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        var icon = new TrayIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = BuildTrayStatusText(),
            Menu = menu,
            IsVisible = true
        };

        icon.Clicked += (_, _) => ShowMainWindow();
        UpdateTrayStatus();
        return icon;
    }

    private void ShowMainWindow()
    {
        if (mainWindow is null)
        {
            return;
        }

        mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
    }

    private void ExitApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        trayIcon?.Dispose();
        trayIcon = null;

        if (mainWindow is not null)
        {
            mainWindow.AllowClose();
            mainWindow.Close();
        }

        desktop.Shutdown();
    }

    private static WindowIcon LoadTrayIcon()
    {
        var assets = AssetLoader.Open(new Uri("avares://WslGui/Assets/avalonia-logo.ico"));
        return new WindowIcon(assets);
    }

    private void UpdateTrayStatus()
    {
        var text = BuildTrayStatusText();
        if (trayIcon is not null)
        {
            trayIcon.ToolTipText = text;
        }

        if (trayStatusItem is not null)
        {
            trayStatusItem.Header = text.Replace(Environment.NewLine, " | ");
        }
    }

    private string BuildTrayStatusText()
    {
        if (mainViewModel is null)
        {
            return "WSL 管理器";
        }

        return $"WSL 管理器{Environment.NewLine}运行中 {mainViewModel.RunningCount} / 保活 {mainViewModel.ActiveKeepAliveCount}{Environment.NewLine}{mainViewModel.LastNotificationText}";
    }

    private static Models.AppSettings LoadStartupSettings(
        ISettingsStore settingsStore,
        IAppLogger logger)
    {
        try
        {
            return settingsStore.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.Error("Startup", "启动设置读取失败。", ex);
            return Models.AppSettings.CreateDefault();
        }
    }
}
