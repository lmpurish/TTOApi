using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class PayrollConfig
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int WarehouseId { get; set; }

        [JsonIgnore]
        public Warehouse Warehouse { get; set; } = null!;

        // Activa/desactiva features por almacén
        public bool EnableWeightExtra { get; set; } = false;
        public bool EnablePenalties { get; set; } = true;
        public bool EnableBonuses { get; set; } = false;

        // Ejemplo: si quieres aplicar un fee fijo por “package damaged”
        [Column(TypeName = "decimal(18,2)")]
        public decimal DefaultPenaltyAmount { get; set; } = 0m;

        // Si quieres tener un “cap” de multas por día/semana
        [Column(TypeName = "decimal(18,2)")]
        public decimal? PenaltyCapPerWeek { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public List<PayrollWeightRule> WeightRules { get; set; } = new();
        public List<PayrollPenaltyRule> PenaltyRules { get; set; } = new();
        public List<PayrollBonusRule> BonusRules { get; set; } = new();
    }
}
