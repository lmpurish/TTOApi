namespace TToApp.Model
{
    public enum ESignMethod { Drawn, Typed, ExternalProvider }

    public class UserDocumentSignature
    {
        public int Id { get; set; }

        public int CompanyDocumentTemplateId { get; set; }
        public CompanyDocumentTemplate Template { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }

        // solo valor (sin FK) para evitar rutas de cascada
        public int CompanyId { get; set; }

        public ESignMethod Method { get; set; } = ESignMethod.Drawn;
        public string? DrawnSignatureImageUrl { get; set; }
        public string? SignedPdfUrl { get; set; }
        public string DocumentHashSha256 { get; set; } = "";
        public DateTime SignedAtUtc { get; set; }
        public string SignerFullName { get; set; } = "";
        public string SignerEmail { get; set; } = "";
        public string? SignerIp { get; set; }
        public string? SignerUserAgent { get; set; }
        public string? GeoInfo { get; set; }
        public string Version { get; set; } = "v1";
    }
}
