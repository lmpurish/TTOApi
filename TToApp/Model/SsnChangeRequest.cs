namespace TToApp.Model
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    namespace TToApp.Model
    {
        public class SsnChangeRequest
        {
            [Key]
            public long Id { get; set; }

            [Required]
            public int UserId { get; set; }

            [ForeignKey(nameof(UserId))]
            public User? User { get; set; }


            [Required]
            [StringLength(1000)]
            public string Reason { get; set; } = string.Empty;


            [Required]
            [StringLength(30)]
            public string Status { get; set; } = "Pending";

            // Pending
            // Approved
            // Rejected
            // Completed
            // Expired
            // NeedsReview


            public DateTime RequestedAt { get; set; }
                = DateTime.UtcNow;


            public int? ReviewedByUserId { get; set; }

            [ForeignKey(nameof(ReviewedByUserId))]
            public User? ReviewedByUser { get; set; }


            public DateTime? ReviewedAt { get; set; }


            [StringLength(1000)]
            public string? StaffNotes { get; set; }


            public DateTime? AuthorizationExpiresAt { get; set; }


            public DateTime? CompletedAt { get; set; }


            // =====================================================
            // DOCUMENT VERIFICATION
            // =====================================================

            [StringLength(30)]
            public string? DocumentVerificationStatus { get; set; }

            // Pending
            // Matched
            // NeedsReview
            // Failed


            public decimal? DocumentVerificationConfidence { get; set; }


            public DateTime? DocumentVerifiedAt { get; set; }


            [StringLength(100)]
            public string? DocumentVerificationProvider { get; set; }


            [StringLength(1000)]
            public string? DocumentVerificationNotes { get; set; }
        }
    }
}
