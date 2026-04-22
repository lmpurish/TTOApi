using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TToApp.Model
{
    public class RentalVehicle
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public Company? Company { get; set; }

        [Required]
        public int MetroId { get; set; }

        [ForeignKey(nameof(MetroId))]
        public Metro? Metro { get; set; }

        [Required]
        [StringLength(120)]
        public string DisplayName { get; set; } = "";

        [StringLength(50)]
        public string? StockNumber { get; set; }

        public int Year { get; set; }

        [StringLength(80)]
        public string Make { get; set; } = "";

        [StringLength(80)]
        public string Model { get; set; } = "";

        [StringLength(50)]
        public string? Trim { get; set; }

        [StringLength(50)]
        public string? Color { get; set; }

        [StringLength(30)]
        public string? Transmission { get; set; }

        [StringLength(30)]
        public string? FuelType { get; set; }

        public int? SeatingCapacity { get; set; }

        [StringLength(500)]
        public string? TrunkNotes { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DailyPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal WeeklyPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DepositAmount { get; set; }

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Draft";
        // Draft, Available, MaintenanceHold, Disabled

        [StringLength(100)]
        public string? Vin { get; set; }

        [StringLength(30)]
        public string? Plate { get; set; }

        [StringLength(150)]
        public string? FacilityLocation { get; set; }

        [StringLength(300)]
        public string? MainImageUrl { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

         public bool GpsInstalled { get; set; }

        public bool DashCamInstalled { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public ICollection<VehicleImage> Images { get; set; } = new List<VehicleImage>();
        public ICollection<VehicleRental> Rentals { get; set; } = new List<VehicleRental>();
    }
}
