namespace TToApp.Model
{
    public class SensitiveDataChangeRequest
    {
        public long Id { get; set; }

        public int UserId { get; set; }

        public string Type { get; set; } = "";
        // SocialSecurity

        public string Reason { get; set; } = "";

        public string Status { get; set; } = "Pending";
        // Pending
        // Approved
        // Rejected
        // Completed

        public DateTime RequestedAt { get; set; }

        public int? ReviewedByUserId { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public string? StaffNotes { get; set; }

        public DateTime? AuthorizationExpiresAt { get; set; }

        public DateTime? CompletedAt { get; set; }
    }
}
