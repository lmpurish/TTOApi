namespace TToApp.Model
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    public class RentalRequest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int VehicleId { get; set; }

        public string RentalType { get; set; } = "Daily"; // Daily / Weekly

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public decimal Price { get; set; }

        public decimal Deposit { get; set; }

        public string PaymentStatus { get; set; } = "Unpaid";

        public string ApprovalStatus { get; set; } = "Submitted";

        public string PickupStatus { get; set; } = "Pending";

        public string DropoffStatus { get; set; } = "Pending";

        public bool LockboxCodeReleased { get; set; }

        public int? AdminReviewerId { get; set; }

        public string? AdminNotes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [ForeignKey("VehicleId")]
        public RentalVehicle? Vehicle { get; set; }
    }
}
