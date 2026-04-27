namespace WslGui.Models;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }

    public bool StartMinimizedToTray { get; set; }

    public int DefaultKeepAliveIntervalSeconds { get; set; } = 60;

    public Dictionary<string, KeepAliveProfile> DistributionProfiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static AppSettings CreateDefault()
    {
        return new AppSettings();
    }
}
