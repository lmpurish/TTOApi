using TToApp.Model;

namespace TToApp.DTOs
{
    public class PackageDto
    {
        public int Id { get; set; }
        public string? Tracking { get; set; }
        public DateTime? IncidentDate { get; set; }
        public string? Status { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? ReviewStatus { get; set; }
        public string? ScanLat { get; set; }
        public string? ScanLon {  get; set; }
        public string? AddrLat { get; set; }
        public string? AddrLon { get; set; }
        public int? DayElapsed { get; set; }
        public RouteDto? Route { get; set; }
        public int? RSP { get; set; }
        public int? warehouseID { get; set; }
    }

    public class RouteDto
    {
        public int Id { get; set; }
        public ZoneDto? Zone{ get; set; }
        public UserDto? User { get; set; }
    }
    public class UserDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? LastName { get; set; }
        public int? WarehouseId { get; set; }
    }

    public class ZoneDto
    { 
        public int Id { get; set; }
        public string? ZoneCode { get; set; }
    }
}
