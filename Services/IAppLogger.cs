using WslGui.Models;

namespace WslGui.Services;

public interface IAppLogger
{
    event EventHandler<AppLogEntry>? EntryLogged;

    string LogDirectory { get; }

    string LogFilePath { get; }

    IReadOnlyList<AppLogEntry> RecentEntries { get; }

    void Info(string category, string message, string? details = null);

    void Warn(string category, string message, string? details = null);

    void Error(string category, string message, string? details = null);

    void Error(string category, string message, Exception exception, string? details = null);

    void Log(
        AppLogLevel level,
        string category,
        string message,
        string? details = null,
        Exception? exception = null);
}
