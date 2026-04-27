using System.Text.Json;
using WslGui.Models;

namespace WslGui.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public JsonSettingsStore()
        : this(GetDefaultSettingsPath())
    {
    }

    public JsonSettingsStore(string settingsPath)
    {
        SettingsPath = settingsPath;
    }

    public string SettingsPath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return AppSettings.CreateDefault();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken);

            return Normalize(settings);
        }
        catch (JsonException)
        {
            BackupCorruptSettingsFile();
            return AppSettings.CreateDefault();
        }
        catch (NotSupportedException)
        {
            BackupCorruptSettingsFile();
            return AppSettings.CreateDefault();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken);
    }

    private static string GetDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(appData, "WslGui", "settings.json");
    }

    private static AppSettings Normalize(AppSettings? settings)
    {
        settings ??= AppSettings.CreateDefault();

        if (settings.DefaultKeepAliveIntervalSeconds < 5)
        {
            settings.DefaultKeepAliveIntervalSeconds = 60;
        }

        settings.DistributionProfiles = new Dictionary<string, KeepAliveProfile>(
            settings.DistributionProfiles ?? new Dictionary<string, KeepAliveProfile>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (name, profile) in settings.DistributionProfiles.ToArray())
        {
            if (string.IsNullOrWhiteSpace(name) || profile is null)
            {
                settings.DistributionProfiles.Remove(name);
                continue;
            }

            profile.Mode = string.IsNullOrWhiteSpace(profile.Mode) ? KeepAliveModes.Pulse : profile.Mode;
            if (profile.IntervalSeconds is < 5)
            {
                profile.IntervalSeconds = null;
            }

            profile.HealthCheck ??= new HealthCheckProfile();
            profile.HealthCheck.Type = string.IsNullOrWhiteSpace(profile.HealthCheck.Type)
                ? HealthCheckTypes.None
                : profile.HealthCheck.Type;

            if (profile.HealthCheck.TimeoutSeconds < 1)
            {
                profile.HealthCheck.TimeoutSeconds = 10;
            }
        }

        return settings;
    }

    private void BackupCorruptSettingsFile()
    {
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = $"{SettingsPath}.corrupt.{timestamp}";
        File.Move(SettingsPath, backupPath, overwrite: true);
    }
}
