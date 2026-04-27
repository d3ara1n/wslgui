namespace WslGui.Models;

public sealed record AppLogEntry(
    DateTimeOffset Timestamp,
    AppLogLevel Level,
    string Category,
    string Message,
    string? Details);
