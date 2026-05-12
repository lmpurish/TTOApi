using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TToApp.Model
{
    public class EarlyWarning
    {
        [Key]
        public long Id { get; set; }

        public long CompanyId { get; set; }
       public int? WarehouseId { get; set; }
        public Warehouse? Warehouse { get; set; }

        [MaxLength(50)]
        public string Type { get; set; } = null!;
        // HiringCapacity, LowDrivers, MissingZones, HighFines

        [MaxLength(20)]
        public string Level { get; set; } = "Warning";
        // Info, Warning, Critical

        public DateOnly ReferenceDate { get; set; }

        public int DaysEvaluated { get; set; } = 1;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BaselineValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CurrentValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? IncreasePercent { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ZScore { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "Open";

        public string? Message { get; set; }

        public string? PayloadJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedAt { get; set; }
        public int? ReviewedBy { get; set; }
        public bool NotificationSent { get; set; } = false;
        public DateTime? NotificationSentAt { get; set; }
    }
}