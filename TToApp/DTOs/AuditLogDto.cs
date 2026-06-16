namespace TToApp.DTOs
{
    public class AuditLogDto
    {
        public int? UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserRole { get; set; }

        public string Action { get; set; } = string.Empty;

        public string? Entity { get; set; }
        public string? EntityId { get; set; }

        public string? Description { get; set; }

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public int? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }

        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
    }
}
