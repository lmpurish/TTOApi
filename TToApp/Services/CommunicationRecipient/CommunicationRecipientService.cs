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
            int? warehouseId,
            string eventType,
            string channel)
        {
            var rules = await _db.CommunicationRecipientRules
                .AsNoTracking()
                .Where(r =>
                    r.IsActive &&
                    r.CompanyId == companyId &&
                    r.EventType == eventType &&
                    r.Channel == channel &&
                    (r.WarehouseId == null || r.WarehouseId == warehouseId))
                .ToListAsync();

            var roles = rules
                .Select(r => r.Role)
                .Distinct()
                .ToList();

            if (!roles.Any())
                return new List<User>();

            return await _db.Users
                .AsNoTracking()
                .Where(u =>
                    u.IsActive &&
                    u.CompanyId == companyId &&
                    u.UserRole.HasValue &&
                    roles.Contains(u.UserRole.Value) &&
                    !string.IsNullOrWhiteSpace(u.Email) &&
                    (
                        u.UserRole == User.Role.Admin ||
                        u.UserRole == User.Role.CompanyOwner ||
                        warehouseId == null ||
                        u.WarehouseId == warehouseId
                    ))
                .GroupBy(u => u.Email!.Trim().ToLower())
                .Select(g => g.First())
                .ToListAsync();
        }
    }
}