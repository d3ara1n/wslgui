using System.Text;
using System.Text.Json;
using WslGui.Models;

namespace WslGui.Services;

public sealed class FileAppLogger : IAppLogger
{
    public const int MaxRecentEntryCount = 500;
    public const long DefaultMaxLogFileBytes = 2 * 1024 * 1024;
    public const int DefaultMaxArchiveFileCount = 3;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly object syncRoot = new();
    private readonly List<AppLogEntry> recentEntries = [];
    private readonly long maxLogFileBytes;
    private readonly int maxArchiveFileCount;

    public FileAppLogger()
        : this(GetDefaultLogDirectory())
    {
    }

    public FileAppLogger(
        string logDirectory,
        long maxLogFileBytes = DefaultMaxLogFileBytes,
        int maxArchiveFileCount = DefaultMaxArchiveFileCount)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("日志目录不能为空。", nameof(logDirectory));
        }

        LogDirectory = logDirectory;
        LogFilePath = Path.Combine(LogDirectory, "app.log");
        this.maxLogFileBytes = Math.Max(1024, maxLogFileBytes);
        this.maxArchiveFileCount = Math.Max(1, maxArchiveFileCount);
    }

    public event EventHandler<AppLogEntry>? EntryLogged;

    public string LogDirectory { get; }

    public string LogFilePath { get; }

    public IReadOnlyList<AppLogEntry> RecentEntries
    {
        get
        {
            lock (syncRoot)
            {
                return recentEntries.ToArray();
            }
        }
    }

    public void Info(string category, string message, string? details = null)
    {
        Log(AppLogLevel.Info, category, message, details);
    }

    public void Warn(string category, string message, string? details = null)
    {
        Log(AppLogLevel.Warn, category, message, details);
    }

    public void Error(string category, string message, string? details = null)
    {
        Log(AppLogLevel.Error, category, message, details);
    }

    public void Error(string category, string message, Exception exception, string? details = null)
    {
        Log(AppLogLevel.Error, category, message, details, exception);
    }

    public void Log(
        AppLogLevel level,
        string category,
        string message,
        string? details = null,
        Exception? exception = null)
    {
        var entry = new AppLogEntry(
            DateTimeOffset.Now,
            level,
            Normalize(category, "General"),
            Normalize(message, "无日志消息。"),
            CombineDetails(details, exception));

        AppLogEntry? fileFailureEntry = null;

        lock (syncRoot)
        {
            AddRecentEntry(entry);

            try
            {
                WriteEntryToFile(entry);
            }
            catch (Exception writeException) when (writeException is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException)
            {
                fileFailureEntry = new AppLogEntry(
                    DateTimeOffset.Now,
                    AppLogLevel.Error,
                    "Logger",
                    "日志文件写入失败。",
                    writeException.ToString());

                AddRecentEntry(fileFailureEntry);
            }
        }

        EntryLogged?.Invoke(this, entry);

        if (fileFailureEntry is not null)
        {
            EntryLogged?.Invoke(this, fileFailureEntry);
        }
    }

    private static string GetDefaultLogDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(appData, "WslGui", "logs");
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? CombineDetails(string? details, Exception? exception)
    {
        if (exception is null)
        {
            return string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        }

        return string.IsNullOrWhiteSpace(details)
            ? exception.ToString()
            : $"{details.Trim()}{Environment.NewLine}{exception}";
    }

    private void AddRecentEntry(AppLogEntry entry)
    {
        recentEntries.Add(entry);

        if (recentEntries.Count > MaxRecentEntryCount)
        {
            recentEntries.RemoveRange(0, recentEntries.Count - MaxRecentEntryCount);
        }
    }

    private void WriteEntryToFile(AppLogEntry entry)
    {
        Directory.CreateDirectory(LogDirectory);

        var line = JsonSerializer.Serialize(new
        {
            timestamp = entry.Timestamp.ToString("O"),
            level = entry.Level.ToString(),
            entry.Category,
            entry.Message,
            entry.Details
        }, JsonOptions) + Environment.NewLine;

        RotateIfNeeded(Utf8NoBom.GetByteCount(line));
        File.AppendAllText(LogFilePath, line, Utf8NoBom);
    }

    private void RotateIfNeeded(int pendingBytes)
    {
        var fileInfo = new FileInfo(LogFilePath);
        if (!fileInfo.Exists || fileInfo.Length + pendingBytes <= maxLogFileBytes)
        {
            return;
        }

        for (var index = maxArchiveFileCount; index >= 1; index--)
        {
            var path = GetArchivePath(index);
            if (!File.Exists(path))
            {
                continue;
            }

            if (index == maxArchiveFileCount)
            {
                File.Delete(path);
                continue;
            }

            File.Move(path, GetArchivePath(index + 1), overwrite: true);
        }

        File.Move(LogFilePath, GetArchivePath(1), overwrite: true);
    }

    private string GetArchivePath(int index)
    {
        return Path.Combine(LogDirectory, $"app.{index}.log");
    }
}
