using System.ComponentModel.DataAnnotations;

namespace TToApp.DTOs
{
    public class CreateVehicleDto
    {
        public int CompanyId { get; set; }
        public int MetroId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? StockNumber { get; set; }
        public int Year { get; set; }
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string? Trim { get; set; }
        public string? Color { get; set; }
        public string? Transmission { get; set; }
        public string? FuelType { get; set; }
        public int? SeatingCapacity { get; set; }
        public string? TrunkNotes { get; set; }
        public decimal DailyPrice { get; set; }
        public decimal WeeklyPrice { get; set; }
        public decimal DepositAmount { get; set; }
        public string? Vin { get; set; }
        public string? Plate { get; set; }
        public string? FacilityLocation { get; set; }
        public string? Notes { get; set; }
        public bool GpsInstalled { get; set; }
        public bool DashCamInstalled { get; set; }
        public string Status { get; set; } = "Draft";

        public List<IFormFile>? Images { get; set; }
    }
}
