namespace TToApp.Contracts
{
    public class CompanyDocsCreateTemplateForm
    {
        public IFormFile PdfFile { get; set; } = default!;

        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "v1";
        public bool IsMandatoryForAllUsers { get; set; } = false;

        public int SignaturePage { get; set; } = 1;
        public float SignatureX { get; set; }
        public float SignatureY { get; set; }
        public float SignatureWidth { get; set; }
        public float SignatureHeight { get; set; }

        public string? RequiredRolesCsv { get; set; }
        public string? FieldsJson { get; set; }
    }
}
