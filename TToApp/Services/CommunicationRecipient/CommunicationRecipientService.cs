using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TToApp.Model;

namespace TToApp.Services.CommunicationRecipient
{

    public class CommunicationRecipientService : ICommunicationRecipientService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<CommunicationRecipientService> _logger;

        public CommunicationRecipientService(ApplicationDbContext db,  ILogger<CommunicationRecipientService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<User>> GetRecipientsForEventAsync(
            int companyId,
            IEnumerable<int>? warehouseIds,
            string eventType,
            string channel)
        {
            var warehouseList = warehouseIds?.ToList();

            var rules = await _db.CommunicationRecipientRules
                .AsNoTracking()
                .Where(r =>
                    r.IsActive &&
                    r.CompanyId == companyId &&
                    r.EventType == eventType &&
                    r.Channel == channel &&
                    (r.WarehouseId == null || (warehouseList != null && warehouseList.Contains(r.WarehouseId.Value))))
                .ToListAsync();

            var roles = rules
                .Select(r => r.Role)
                .Distinct()
                .ToList();

            var roleUsers = roles.Any()
                ? await _db.Users
                    .AsNoTracking()
                    .Where(u =>
                        u.IsActive &&
                        u.CompanyId == companyId &&
                        u.UserRole.HasValue &&
                        roles.Contains(u.UserRole.Value) &&
                        !string.IsNullOrWhiteSpace(u.Email) &&
                        (
                            warehouseList == null ||
                            u.UserRole == User.Role.Admin ||
                            u.UserRole == User.Role.CompanyOwner ||
                            u.WarehouseId.HasValue && warehouseList.Contains(u.WarehouseId.Value)
                        ))
                    .ToListAsync()
                : new List<User>();

            var permitUsers = new List<User>();
            if (warehouseList is { Count: > 0 })
            {
                var permitUserIds = await _db.Permits
                    .AsNoTracking()
                    .Where(p => p.UserPermit == Permit.Notification && warehouseList.Contains(p.WarehouseId))
                    .Select(p => p.UserId)
                    .Distinct()
                    .ToListAsync();

                if (permitUserIds.Any())
                    permitUsers = await _db.Users
                        .AsNoTracking()
                        .Where(u =>
                            u.IsActive &&
                            u.CompanyId == companyId &&
                            !string.IsNullOrWhiteSpace(u.Email) &&
                            permitUserIds.Contains(u.Id))
                        .ToListAsync();
            }

            // (Fix 4) Guardar contra emails nulos antes del GroupBy
            return roleUsers
                .Union(permitUsers)
                .Where(u => !string.IsNullOrWhiteSpace(u.Email))
                .GroupBy(u => u.Email!.Trim().ToLower())
                .Select(g => g.First())
                .ToList();
        }
    }
}