using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class PayrollWeightRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PayrollConfigId { get; set; }

        [JsonIgnore]
        public PayrollConfig PayrollConfig { get; set; } = null!;

        // Rango de peso (en LB o KG). Recomiendo LB si tus reportes vienen en LB. 

        [Column(TypeName = "decimal(18,2)")]
        public decimal MinWeight { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MaxWeight { get; set; } // null = sin tope

        // Extra por paquete en ese rango

        [Column(TypeName = "decimal(18,2)")]
        public decimal ExtraAmount { get; set; }

        public bool IsActive { get; set; } = true;

        // Para ordenar (por si quieres control)
        public int Priority { get; set; } = 0;
    }
}
