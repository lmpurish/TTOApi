namespace TToApp.Contracts
{
    public class DocumentsSignDocumentRequest
    {
        public int TemplateId { get; set; }
        public string SignerFullName { get; set; } = "";
        public string SignerEmail { get; set; } = "";
        public string PdfHashSha256 { get; set; } = "";
        public string? DrawnSignatureImageBase64 { get; set; }
        public string? SignedPdfBase64 { get; set; }
    }
    public record DocumentsSignResponse(int Id, string? SignedPdfUrl, string? DrawnSignatureImageUrl);
}
