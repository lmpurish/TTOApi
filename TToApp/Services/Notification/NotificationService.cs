using TToApp.Model;
using Microsoft.EntityFrameworkCore;
namespace TToApp.Services.Notifications;



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
     
    public async Task unassingnedZonesByManagerOntrac()
    {
        var rows = await (
            from u in _context.Set<User>().AsNoTracking()
            where u.UserRole == global::User.Role.Manager
            && u.IsActive 
            && u.WarehouseId != null

            join r in _context.Set<Routes>().AsNoTracking()
                on u.WarehouseId equals r.WarehouseId

            join w in _context.Set<Warehouse>().AsNoTracking()
                on r.WarehouseId equals (int?)w.Id

            where r.ZoneId == null
            && (w.Company ?? "").Trim().ToLower() == "ontrac"

            group r by new
            {
                u.Id,
                WarehouseId = u.WarehouseId!.Value,
                Day = r.Date.Date
            } into g

            select new RouteWithoutZoneByManagerDto
            {
                UserId = g.Key.Id,
                WarehouseId = g.Key.WarehouseId,
                Date = g.Key.Day,
                RoutesWithoutZone = g.Count()
            }
        ).ToListAsync();

        var notifications = rows
            .GroupBy(x => x.UserId)
            .Select(g =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Summarised:");

                foreach (var item in g.OrderBy(x => x.Date))
                    sb.AppendLine($"{item.Date:MMM dd yyyy}: {item.RoutesWithoutZone}");

                return new Notification
                {
                    UserId = g.Key,
                    Title = "Unassigned Zones/Routes By Date (Ontrac)",
                    Message = sb.ToString().Trim(),
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false,
                    Type = NotificationType.System
                };
            })
            .ToList();

        if (notifications.Count > 0)
        {
            _context.Set<Notification>().AddRange(notifications);
            await _context.SaveChangesAsync();
        }

        //return notifications;
    
}




}