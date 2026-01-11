namespace TToApp.Contracts
{
    public record DocumentsTemplateCreateResponse(int Id, string FileUrl, string Version, string PdfSha256);

    public record DocumentsTemplateDto(
        int Id, string Title, string Description, string FileUrl, string Version,
        int SignaturePage, float SignatureX, float SignatureY, float SignatureWidth, float SignatureHeight);
    public class DocumentsAssignTemplateRequest
    {
        public bool ToAllUsers { get; set; } = true;
        public string? RolesCsv { get; set; }
        public DateTime? DueDateUtc { get; set; }
    }
}
