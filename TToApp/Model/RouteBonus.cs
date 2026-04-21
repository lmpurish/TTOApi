using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TToApp.Model
{
    public class RouteBonus
    {
        public int Id { get; set; }

        [Required]
        public int RouteId { get; set; }
        public Routes Route { get; set; } = null!;

        [Required]
        public RouteBonusType Type { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public string? Note { get; set; }

        public int AssignedByUserId { get; set; }
        public User AssignedByUser { get; set; } = null!;

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;
    }
}
public enum RouteBonusType
{
    Unknown = 0,
    Gas = 1,
    Rescue = 2,
    ExtraHelp = 3,
    SpecialRoute = 4
}
