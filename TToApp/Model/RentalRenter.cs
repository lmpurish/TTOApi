using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class RentalRenter
    {
        [Key]
        public int Id { get; set; }

        // Si también es un usuario interno del sistema
        public int? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = "";

        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = "";

        [Required]
        [EmailAddress]
        [StringLength(150)]
        public string Email { get; set; } = "";

        [StringLength(30)]
        public string? PhoneNumber { get; set; }

        [StringLength(50)]
        public string? DriverLicenseNumber { get; set; }

        public DateOnly? DriverLicenseExpiration { get; set; }

        [StringLength(50)]
        public string? IdentificationNumber { get; set; }

        [StringLength(250)]
        public string? Address { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(100)]
        public string? State { get; set; }

        [StringLength(20)]
        public string? ZipCode { get; set; }

        [StringLength(100)]
        public string? EmergencyContactName { get; set; }

        [StringLength(30)]
        public string? EmergencyContactPhone { get; set; }

        public bool IsActive { get; set; } = true;

        [StringLength(1000)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [JsonIgnore]
        public ICollection<VehicleRental> VehicleRentals { get; set; } = new List<VehicleRental>();
    }
}