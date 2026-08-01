using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class UserWarehouse
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [JsonIgnore]
        public User User { get; set; } = null!;

        [Required]
        public int WarehouseId { get; set; }

        public Warehouse Warehouse { get; set; } = null!;

        public bool IsPrimary { get; set; } = false;

        public bool IsActive { get; set; } = true;

        public DateOnly? StartDate { get; set; }

        public DateOnly? EndDate { get; set; }
            // Esta asignación paga salario diario de Manager
        public bool PaysManagerDailySalary { get; set; } = false;

        // Opcional: salario específico para este almacén
        public decimal? ManagerDailyRate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? CreatedBy { get; set; }
    }
}
