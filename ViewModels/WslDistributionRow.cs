using CommunityToolkit.Mvvm.ComponentModel;
using WslGui.Models;

namespace WslGui.ViewModels;

public sealed partial class WslDistributionRow : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private bool isDefault;

    [ObservableProperty]
    private string state = "Unknown";

    [ObservableProperty]
    private int? version;

    [ObservableProperty]
    private bool isKeepAliveActive;

    [ObservableProperty]
    private string keepAliveState = "未保活";

    [ObservableProperty]
    private string keepAliveMode = "无";

    [ObservableProperty]
    private int heartbeatCount;

    [ObservableProperty]
    private int errorCount;

    [ObservableProperty]
    private string lastPingText = "从未";

    [ObservableProperty]
    private string lastError = string.Empty;

    public string DefaultMarker => IsDefault ? "默认" : "";

    public string VersionText => Version?.ToString() ?? "-";

    public bool IsRunning =>
        State.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
        State.Contains("运行", StringComparison.OrdinalIgnoreCase);

    public string ErrorSummary => string.IsNullOrWhiteSpace(LastError) ? "无" : LastError;

    public void Apply(WslDistribution distribution)
    {
        Name = distribution.Name;
        IsDefault = distribution.IsDefault;
        State = distribution.State;
        Version = distribution.Version;
    }

    partial void OnIsDefaultChanged(bool value)
    {
        OnPropertyChanged(nameof(DefaultMarker));
    }

    partial void OnStateChanged(string value)
    {
        OnPropertyChanged(nameof(IsRunning));
    }

    partial void OnVersionChanged(int? value)
    {
        OnPropertyChanged(nameof(VersionText));
    }

    partial void OnLastErrorChanged(string value)
    {
        OnPropertyChanged(nameof(ErrorSummary));
    }
}
