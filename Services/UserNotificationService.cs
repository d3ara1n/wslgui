namespace WslGui.Services;

public sealed class UserNotificationService : IUserNotificationService
{
    public event EventHandler<UserNotificationRequestedEventArgs>? NotificationRequested;

    public void Info(string title, string message)
    {
        Request(UserNotificationLevel.Info, title, message);
    }

    public void Warn(string title, string message)
    {
        Request(UserNotificationLevel.Warn, title, message);
    }

    public void Error(string title, string message)
    {
        Request(UserNotificationLevel.Error, title, message);
    }

    public void Request(UserNotification notification)
    {
        NotificationRequested?.Invoke(this, new UserNotificationRequestedEventArgs(notification));
    }

    private void Request(UserNotificationLevel level, string title, string message)
    {
        Request(new UserNotification(
            level,
            Normalize(title, "WSL 管理器"),
            Normalize(message, string.Empty),
            DateTimeOffset.Now));
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
