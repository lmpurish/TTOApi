namespace TToApp.Model
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    public class AuditLogs
    {
        [Key]
        public int Id { get; set; }

        public int? UserId { get; set; }

        [MaxLength(200)]
        public string? UserName { get; set; }

        [MaxLength(50)]
        public string? UserRole { get; set; }

        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Entity { get; set; }

        [MaxLength(100)]
        public string? EntityId { get; set; }

        public string? Description { get; set; }

        public string? OldValue { get; set; }

        public string? NewValue { get; set; }

        [MaxLength(100)]
        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        [MaxLength(100)]
        public string? WarehouseName { get; set; }

        public int? WarehouseId { get; set; }

        [MaxLength(100)]
        public string? CompanyName { get; set; }

        public int? CompanyId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
