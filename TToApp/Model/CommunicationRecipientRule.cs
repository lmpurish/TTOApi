using System.ComponentModel.DataAnnotations;

namespace TToApp.Model
{
    public class CommunicationRecipientRule
    {
        [Key]
        public long Id { get; set; }

        public int CompanyId { get; set; }
        public int? WarehouseId { get; set; } // null = global

        [MaxLength(60)]
        public string EventType { get; set; } = null!;

        [MaxLength(20)]
        public string Channel { get; set; } = "Email";

        public User.Role Role { get; set; }

        public bool OnlyCritical { get; set; } = false;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}