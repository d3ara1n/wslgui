using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using WslGui.ViewModels;
using WslGui.Views;

namespace WslGui;

public partial class App : Application
{
    private TrayIcon? trayIcon;
    private MainWindow? mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

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

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => ExitApplication(desktop);

        menu.Items.Add(showItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        var icon = new TrayIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = "WSL 管理器",
            Menu = menu,
            IsVisible = true
        };

        icon.Clicked += (_, _) => ShowMainWindow();
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
}
