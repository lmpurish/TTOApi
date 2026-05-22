using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TToApp.Model
{
    public class Incidence
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int RouteId { get; set; }

        [ForeignKey("RouteId")]
        public Routes? Route { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [Required]
        public IncidenceType Type { get; set; }

        public string? Description { get; set; }

        public string? ImageName { get; set; }
        public string? ImageUrl { get; set; }

        [Required]
        public DateTime OccurredAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum IncidenceType
    {
        OpenedPackage,    // Package found opened or tampered
        WrongPosition,    // Package left in wrong location
        DamagedPackage,   // Package has visible physical damage
        MissingPackage,   // Package cannot be located
        WrongAddress,     // Delivered to incorrect address
        RefusedDelivery,  // Customer refused to accept the package
        Other
    }
}
