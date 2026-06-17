using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class ZoneWeightRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ZoneId { get; set; }

        [JsonIgnore]
        public Zone Zone { get; set; } = null!;

        [Column(TypeName = "decimal(10,2)")]
        public decimal MinWeight { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal? MaxWeight { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal ExtraAmount { get; set; }

        public bool IsActive { get; set; } = true;
        public int Priority { get; set; } = 0;
        public int Version { get; set; } = 1;
        public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
        public DateTime? EffectiveTo { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? CreatedBy { get; set; }
    }
}
