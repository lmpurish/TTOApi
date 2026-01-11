using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class PayrollBonusRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PayrollConfigId { get; set; }

        [JsonIgnore]
        public PayrollConfig PayrollConfig { get; set; } = null!;

        [Required]
        public BonusType Type { get; set; }

        // Ejemplo: threshold = 98.0 (si aplica)
        [Column(TypeName = "decimal(18,2)")]
        public decimal? Threshold { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public enum BonusType
    {
        Unknown = 0,
        HighOnTime = 1,
        HighStops = 2,
        LowCnl = 3
    }
}
