# WSL 管理器

基于 .NET 10、Avalonia 12 和 Huskui.Avalonia 的 WSL 桌面管理工具。

## 功能

- 查看 WSL 发行版列表、默认发行版、运行状态和 WSL 版本。
- 对选中的发行版执行启动、停止操作。
- 查看 `wsl.exe --status` 的主机侧状态信息。
- 统计发行版总数、运行中数量、停止数量和启用保活的实例数量。
- 后台托盘运行，关闭主窗口不会停止保活任务。
- 支持开机自启动和启动后最小化到托盘。
- 支持按发行版保存保活策略，并在启动后自动恢复。
- 提供两种保活策略：
  - 周期心跳：按指定间隔向目标发行版发送轻量 shell 心跳命令。
  - 驻留进程：在目标发行版内启动一个低频 `sleep` 循环进程，适合让 WSL 作为服务保持运行。
- 提供日志面板和失败通知状态。
- 支持采集实例 uptime、进程数、内存、load average 等轻量状态。
- 支持 shell 命令、TCP 端口、systemd service 健康检查。
- 支持安装容器内 helper 到 `~/.wslgui/helper.sh`。
- 支持快速打开 Windows Terminal、WSL 目录和复制 UNC 路径。
- 支持导出、导入和带确认保护的注销操作。

## 运行

```powershell
dotnet run
```

## 构建

```powershell
dotnet build
```

## 发布单文件

```powershell
.\scripts\publish-single.ps1
```

默认发布 `win-x64`、Release、自包含、单文件可执行程序，输出到 `artifacts\publish\win-x64-single\WslGui.exe`。

## 说明

保活策略只在本程序运行期间生效。点击窗口关闭按钮只会隐藏主界面，托盘菜单仍可重新打开窗口；只有通过托盘菜单选择“退出”时，程序才会取消心跳任务，并终止由本程序启动的驻留保活进程。

当前实现通过 `wsl.exe` 与 WSL 交互，不需要管理员权限。配置保存在 `%APPDATA%\WslGui\settings.json`，日志保存在 `%APPDATA%\WslGui\logs\app.log`。
