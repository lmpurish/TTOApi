using System.ComponentModel.DataAnnotations;

namespace TToApp.Model
{
    public class Payment
    {
        [Key]
        public int Id { get; set; }

        public int RentalRequestId { get; set; }

        public decimal Amount { get; set; }

        public string PaymentType { get; set; } = ""; // Deposit / Rental

        public string Status { get; set; } = "Pending";

        public string? StripePaymentId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
