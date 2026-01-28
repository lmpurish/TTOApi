using TToApp.Model;

public interface INotificationService
{
    Task NotifyAsync(int userId, string title, string? message, NotificationType type, string? url = null, string? source = null);
    Task unassingnedZonesByManagerOntrac();
}