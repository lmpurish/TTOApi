using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class UserProfile
    {
        [Key]
        [ForeignKey("User")]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; }

        public string? DriverLicenseNumber { get; set; }
        public DateOnly? ExpDriverLicense { get; set; }
        public string? DrivingLicenseUrl { get; set; } // frontal o PDF; si quieres, separa Front/Back
        public string ? Address { get; set; }
        public string ? City    { get; set; }
        public string ? State { get; set; }
        public string ? ZipCode { get; set; }

        // SSN seguro
        public string? SsnEncrypted { get; set; }
        public string? SsnLast4 { get; set; }
        public DateTime? SsnUpdatedAt { get; set; }

        // (Opcional) archivo adjunto con SSN, igual privado
        public string? SocialSecurityUrl { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        [Phone, StringLength(20)]
        public string? PhoneNumber { get; set; }
        
        public DateOnly? ExpInsurance {  get; set; }
        public string? InsuranceUrl {  get; set; }

        public User? User { get; set; }
    }

}
