using TToApp.Model;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly EmailService _emailService;

    public NotificationService(ApplicationDbContext context, EmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    public async Task NotifyAsync(int userId, string title, string? message, NotificationType type, string? url = null, string? source = null)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            Url = url,
            Source = source,
            CreatedAt = DateTime.Now,
            IsRead = false
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
    }

    public async Task NotificationByPermit (int warehouseId, string message)
    {



    }
}