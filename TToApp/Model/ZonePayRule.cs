using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public enum PaymentType
    {
        PerRoute,
        PerStop,
        Mixed,
        PerBlock
    }

    public class ZonePayRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ZoneId { get; set; }

        [JsonIgnore]
        public Zone Zone { get; set; } = null!;

        public PaymentType PaymentType { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? BaseAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ExtraAmount { get; set; }

        public int? MinPackages { get; set; }
        public int? MaxPackages { get; set; }

        public bool UseDriverRateForExtra { get; set; } = true;

        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;

        public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
        public DateTime? EffectiveTo { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? CreatedBy { get; set; }
    }
}
