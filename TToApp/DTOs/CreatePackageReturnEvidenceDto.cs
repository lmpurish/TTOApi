namespace TToApp.DTOs
{
    public class CreatePackageReturnEvidenceDto
    {
        public string Tracking { get; set; } = string.Empty;

        public int? DriverId { get; set; }
        public string? DriverPhone { get; set; }
        public string? DriverName { get; set; }

        public int? WarehouseId { get; set; }

        public string? Reason { get; set; }
        public string? Message { get; set; }

        public string? ImageBase64 { get; set; }
        public string? ImageUrl { get; set; }
    }
}
