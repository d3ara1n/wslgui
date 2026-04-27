namespace WslGui.Services;

public interface IUserNotificationService
{
    event EventHandler<UserNotificationRequestedEventArgs>? NotificationRequested;

    void Info(string title, string message);

    void Warn(string title, string message);

    void Error(string title, string message);

    void Request(UserNotification notification);
}

public sealed record UserNotification(
    UserNotificationLevel Level,
    string Title,
    string Message,
    DateTimeOffset Timestamp);

public enum UserNotificationLevel
{
    Info,
    Warn,
    Error
}

public sealed class UserNotificationRequestedEventArgs : EventArgs
{
    public UserNotificationRequestedEventArgs(UserNotification notification)
    {
        Notification = notification;
    }

    public UserNotification Notification { get; }
}
