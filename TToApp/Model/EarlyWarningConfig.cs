using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TToApp.Model
{
    public class EarlyWarningConfig
    {
        [Key]
        public long Id { get; set; }

        public int CompanyId { get; set; }
        public int? WarehouseId { get; set; } // null = global

        [MaxLength(50)]
        public string Type { get; set; } = null!; // HiringCapacity

        [Column(TypeName = "decimal(18,2)")]
        public decimal ThresholdPercent { get; set; } = 25;

        public int DaysForCritical { get; set; } = 3;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}