using Microsoft.EntityFrameworkCore;
using TToApp.Constants;
using TToApp.Model;

namespace TToApp.Services.EarlyWarnings
{
    public class EarlyWarningNotificationService : IEarlyWarningNotificationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<EarlyWarningNotificationService> _logger;
        private readonly EmailService _emailService;

        public EarlyWarningNotificationService(
            ApplicationDbContext db,
            ILogger<EarlyWarningNotificationService> logger,
            EmailService emailService)
        {
            _db = db;
            _logger = logger;
            _emailService = emailService;
        }
        public async Task NotifyPendingHiringWarningsAsync()
        {
            var warnings = await _db.EarlyWarnings
                .Include(x => x.Warehouse)
                .Where(x =>
                    !x.NotificationSent &&
                    x.Type == EarlyWarningTypes.HiringCapacity &&
                    x.Status == "Open")
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            if (!warnings.Any())
            {
                _logger.LogInformation("📭 There are no pending EarlyWarnings.");
                return;
            }

            foreach (var warning in warnings)
            {
                try
                {
                    var inAppUsers = (await GetRecipientsAsync(warning, CommunicationChannels.InApp))
                        .GroupBy(u => u.Id)
                        .Select(g => g.First())
                        .ToList();

                    var emailUsers = (await GetRecipientsAsync(warning, CommunicationChannels.Email))
                        .Where(u => !string.IsNullOrWhiteSpace(u.Email))
                        .GroupBy(u => u.Id)
                        .Select(g => g.First())
                        .ToList();

                    foreach (var user in inAppUsers)
                    {
                        var exists = await _db.Notifications.AnyAsync(n =>
                            n.UserId == user.Id &&
                            n.Source == $"EW-{warning.Id}");

                        if (exists)
                            continue;

                        _db.Notifications.Add(new Notification
                        {
                            UserId = user.Id,
                            Title = warning.Level == EarlyWarningLevels.Critical
                                ? "🚨 Critical Hiring Alert"
                                : "⚠️ Hiring Alert",
                            Message = warning.Message,
                            Type = warning.Level == EarlyWarningLevels.Critical
                                ? NotificationType.Error
                                : NotificationType.Warning,
                            IsRead = false,
                            CreatedAt = DateTime.Now,
                            Source = $"EW-{warning.Id}",
                            Url = $"/early-warnings/{warning.Id}"
                        });
                    }

                    if (warning.Level == EarlyWarningLevels.Critical)
                    {
                        var warehouseName = warning.Warehouse?.Name ?? "N/A";

                        var placeholders = new Dictionary<string, string>
                        {
                            { "Warehouse", warehouseName },
                            { "ReferenceDate", warning.ReferenceDate.ToString("yyyy-MM-dd") },
                            { "CurrentValue", warning.CurrentValue.ToString("0.##") },
                            { "BaselineValue", warning.BaselineValue.ToString("0.##") },
                            { "IncreasePercent", warning.IncreasePercent?.ToString("0.##") ?? "0" }
                        };

                        foreach (var user in emailUsers)
                        {
                            await _emailService.SendEmailAsync(
                                toEmail: user.Email!,
                                subject: "Critical Hiring Alert!",
                                "CriticalHiringAlert.cshtml",
                                placeholders: placeholders,
                                copy: false
                            );

                            _logger.LogInformation(
                                "📧 Sending email for EarlyWarning {Id} to {Email}",
                                warning.Id,
                                user.Email);
                        }
                    }

                    warning.NotificationSent = true;
                    warning.NotificationSentAt = DateTime.UtcNow;

                    _logger.LogInformation(
                        "📤 Notification processed for EarlyWarning {Id}",
                        warning.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "❌ Error notifying EarlyWarning {Id}",
                        warning.Id);
                }
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("✅ Notifications completed.");
        }

       public async Task NotifyPendingMissingPackagesWarningsAsync()
        {
            var warnings = await _db.EarlyWarnings
                .Include(x => x.Warehouse)
                .Where(x =>
                    !x.NotificationSent &&
                    x.Type == EarlyWarningTypes.MissingDailyPackages &&
                    x.Status == "Open")
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            if (!warnings.Any())
            {
                _logger.LogInformation("📭 There are no pending MissingDailyPackages warnings.");
                return;
            }

            foreach (var warning in warnings)
            {
                try
                {
                    var inAppUsers = (await GetRecipientsAsync(warning, CommunicationChannels.InApp))
                        .GroupBy(u => u.Id)
                        .Select(g => g.First())
                        .ToList();

                    var emailUsers = (await GetRecipientsAsync(warning, CommunicationChannels.Email))
                        .Where(u => !string.IsNullOrWhiteSpace(u.Email))
                        .GroupBy(u => u.Email!.Trim().ToLower())
                        .Select(g => g.First())
                        .ToList();

                    foreach (var user in inAppUsers)
                    {
                        var exists = await _db.Notifications.AnyAsync(n =>
                            n.UserId == user.Id &&
                            n.Source == $"EW-{warning.Id}");

                        if (exists)
                            continue;

                        _db.Notifications.Add(new Notification
                        {
                            UserId = user.Id,
                            Title = "⚠️ Missing Daily Packages Alert",
                            Message = warning.Message,
                            Type = NotificationType.Warning,
                            IsRead = false,
                            CreatedAt = DateTime.Now,
                            Source = $"EW-{warning.Id}",
                            Url = $"/early-warnings/{warning.Id}"
                        });
                    }

                    var warehouseName = warning.Warehouse?.Name ?? "N/A";
                    var companyName = warning.Warehouse?.Company ?? "N/A";

                    var placeholders = new Dictionary<string, string>
                    {
                        { "Warehouse", warehouseName },
                        { "MissingDate", warning.ReferenceDate.ToString("yyyy-MM-dd") },
                        { "Company", companyName },
                        { "DetectedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") },
                        { "Url", $"https://app.tto.com/early-warnings/{warning.Id}" }
                    };

                    foreach (var user in emailUsers)
                    {
                        await _emailService.SendEmailAsync(
                            toEmail: user.Email!,
                            subject: "Missing Daily Packages Alert!",
                            "MissingDailyPackageAlert.cshtml",
                            placeholders: placeholders,
                            copy: false
                        );

                        _logger.LogInformation(
                            "📧 Sending email for MissingPackages {Id} to {Email}",
                            warning.Id,
                            user.Email);
                    }

                    warning.NotificationSent = true;
                    warning.NotificationSentAt = DateTime.UtcNow;

                    _logger.LogInformation(
                        "📤 MissingDailyPackages notification processed for EarlyWarning {Id}",
                        warning.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "❌ Error notifying MissingDailyPackages EarlyWarning {Id}",
                        warning.Id);
                }
            }

            await _db.SaveChangesAsync();
        }

        private async Task<List<User>> GetRecipientsAsync(
            EarlyWarning warning,
            string channel)
        {
            var rules = await _db.CommunicationRecipientRules
                .Where(r =>
                    r.IsActive &&
                    r.CompanyId == warning.CompanyId &&
                    r.EventType == warning.Type &&
                    r.Channel == channel &&
                    (r.WarehouseId == null || r.WarehouseId == warning.WarehouseId) &&
                    (!r.OnlyCritical || warning.Level == EarlyWarningLevels.Critical))
                .ToListAsync();

            var roles = rules
                .Select(r => r.Role)
                .Distinct()
                .ToList();

            var roleUsers = roles.Any()
                ? await _db.Users
                    .Where(u =>
                        u.IsActive &&
                        u.CompanyId == warning.CompanyId &&
                        u.UserRole.HasValue && roles.Contains(u.UserRole.Value) &&
                        (
                            u.UserRole == User.Role.Admin ||
                            warning.WarehouseId == null ||
                            u.WarehouseId == warning.WarehouseId
                        ))
                    .ToListAsync()
                : new List<User>();

            var permitUsers = new List<User>();
            if (warning.WarehouseId.HasValue)
            {
                var permitUserIds = await _db.Permits
                    .Where(p => p.WarehouseId == warning.WarehouseId.Value && p.UserPermit == Permit.Notification)
                    .Select(p => p.UserId)
                    .ToListAsync();

                if (permitUserIds.Any())
                    permitUsers = await _db.Users
                        .Where(u => u.IsActive && u.CompanyId == warning.CompanyId && permitUserIds.Contains(u.Id))
                        .ToListAsync();
            }

            return roleUsers
                .Union(permitUsers)
                .GroupBy(u => u.Id)
                .Select(g => g.First())
                .ToList();
        }

    }
}