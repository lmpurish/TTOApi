namespace TToApp.Model
{
    public class CompanyDocumentAssignment
    {
        public int Id { get; set; }
        public int CompanyDocumentTemplateId { get; set; }
        public CompanyDocumentTemplate Template { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }

        public bool IsRequired { get; set; } = true;
        public DateTime? DueDateUtc { get; set; }
        public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
        public bool Revoked { get; set; } = false;
    }
}
