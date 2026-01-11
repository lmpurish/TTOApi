using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class PayrollPenaltyRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PayrollConfigId { get; set; }

        [JsonIgnore]
        public PayrollConfig PayrollConfig { get; set; } = null!;

        [Required]
        public PenaltyType Type { get; set; }

        // Amount fijo por incidente (ej: -$5 por “Damaged”)
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        // Opcional: si quieres “por ocurrencia” o “una vez por ruta”
        public bool ApplyPerOccurrence { get; set; } = true;

        // Opcional: límite de veces por día/semana (para no matar al driver)
        public int? MaxOccurrencesPerWeek { get; set; }

        public bool IsActive { get; set; } = true;
    }
    public enum PenaltyType
    {
        Unknown = 0,
        DeliveryError = 1,
        Damaged = 2,
        WrongAddress = 3,
        LateDelivery = 4,
        NoScan = 5,
        ManualEntry = 6,
        CustomerComplaint = 7
    }
}
