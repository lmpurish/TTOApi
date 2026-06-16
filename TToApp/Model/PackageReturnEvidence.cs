using System.IO.Packaging;

namespace TToApp.Model
{
    public class PackageReturnEvidence
    {
        public int Id { get; set; }

        public string Tracking { get; set; } = string.Empty;

        public int? PackageId { get; set; }
        public Packages? Package { get; set; }

        public int? DriverId { get; set; }
        public User? Driver { get; set; }

        public int? WarehouseId { get; set; }
        public Warehouse? Warehouse { get; set; }

        public string? DriverPhone { get; set; }
        public string? DriverName { get; set; }

        public string? Reason { get; set; }
        public string? Message { get; set; }

        public string ImageUrl { get; set; } = string.Empty;

        public string Source { get; set; } = "WhatsAppBot";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public bool Reviewed { get; set; } = false;
        public bool IsDriverFault { get; set; } = false;

        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
    }
}
