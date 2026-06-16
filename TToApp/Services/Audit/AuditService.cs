using System;
using TToApp.DTOs;
using TToApp.Model;
using System.Text.Json;

namespace TToApp.Services.Audit
{
    public class AuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(AuditLogDto dto)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            var audit = new AuditLogs
            {
                UserId = dto.UserId,
                UserName = dto.UserName,
                UserRole = dto.UserRole,

                Action = dto.Action,

                Entity = dto.Entity,
                EntityId = dto.EntityId,

                Description = dto.Description,

                OldValue = dto.OldValue,
                NewValue = dto.NewValue,

                WarehouseId = dto.WarehouseId,
                WarehouseName = dto.WarehouseName,

                CompanyId = dto.CompanyId,
                CompanyName = dto.CompanyName,

                IpAddress = httpContext?.Connection?.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers["User-Agent"].ToString(),

                CreatedAt = DateTime.UtcNow
            };

            _context.AuditLogs.Add(audit);
            await _context.SaveChangesAsync();
        }

        public async Task LogChangeAsync(
            AuditLogDto dto,
            object? oldObject,
            object? newObject)
        {
            dto.OldValue = oldObject == null
                ? null
                : JsonSerializer.Serialize(oldObject);

            dto.NewValue = newObject == null
                ? null
                : JsonSerializer.Serialize(newObject);

            await LogAsync(dto);
        }
    }
}
