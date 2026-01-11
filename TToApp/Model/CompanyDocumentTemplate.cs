namespace TToApp.Model
{
    public class CompanyDocumentTemplate
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public Company Company { get; set; }

        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string FileUrl { get; set; } = "";
        public string Version { get; set; } = "v1";
        public bool IsActive { get; set; } = true;
        public bool RequireSignature { get; set; } = true;
        public bool IsMandatoryForAllUsers { get; set; } = false;
        public string? RequiredRolesCsv { get; set; }

        public int SignaturePage { get; set; } = 1;
        public float SignatureX { get; set; } = 100;
        public float SignatureY { get; set; } = 100;
        public float SignatureWidth { get; set; } = 200;
        public float SignatureHeight { get; set; } = 50;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int CreatedByUserId { get; set; }
        public string? FieldsJson { get; set; } = "[]"; // lista de TemplateFieldDto serializada
        public int PageCount { get; set; }
    }
}
