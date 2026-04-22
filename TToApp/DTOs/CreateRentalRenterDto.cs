using System.ComponentModel.DataAnnotations;

namespace TToApp.DTOs
{
    public class CreateRentalRenterDto
    {
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

        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }

        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }

        public string? Notes { get; set; }
    }
}