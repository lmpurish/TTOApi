using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TToApp.Model
{
    public class VehicleRental
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public int RentalVehicleId { get; set; }

        [ForeignKey(nameof(RentalVehicleId))]
        public RentalVehicle? RentalVehicle { get; set; }

        [Required]
        public int RentalRenterId { get; set; }

        [ForeignKey(nameof(RentalRenterId))]
        public RentalRenter? RentalRenter { get; set; }

        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DailyPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal WeeklyPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DepositAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [StringLength(30)]
        public string Status { get; set; } = "Reserved";
        // Reserved, Active, Completed, Cancelled

        [StringLength(1000)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public int StartMileage { get; set; }
        public int? EndMileage { get; set; }
    }
}