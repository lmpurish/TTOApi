using TToApp.Model;

namespace TToApp.DTOs
{
    public class PackageReviewDTO
    {
        public int Id { get; set; }

        public int PackageId { get; set; }
      
        public string? ImageName { get; set; } = string.Empty;     // ej: "evidence1.jpg"
        public string? ImageUrl { get; set; } = string.Empty;      // ej: "/uploads/evidences/123.jpg" o URL pública

        public string? UploadedBy { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public string? Description { get; set; }  // Breve texto explicando la imagen
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }
}
