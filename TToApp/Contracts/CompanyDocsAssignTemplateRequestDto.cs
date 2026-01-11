namespace TToApp.Contracts
{
    public record CompanyDocsTemplateCreateResponseDto(int Id, string FileUrl, string Version, string PdfSha256);

    public record CompanyDocsTemplateDto(
        int Id, string Title, string Description, string FileUrl, string Version,
        int SignaturePage, float SignatureX, float SignatureY, float SignatureWidth, float SignatureHeight);

    public class CompanyDocsAssignTemplateRequestDto
    {
        public bool ToAllUsers { get; set; } = true;
        public string? RolesCsv { get; set; }
        public DateTime? DueDateUtc { get; set; }
    }
}
