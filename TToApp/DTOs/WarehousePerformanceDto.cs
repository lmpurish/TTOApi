using System.ComponentModel.DataAnnotations;
using TToApp.Model;

namespace TToApp.DTOs
{
    public class WarehousePerformanceDto
    {
        public int WarehouseId { get; set; }
        public string Warehouse { get; set; } = "";
        public string City { get; set; } = "";
        public int Volumen { get; set; }
        public int Stops { get; set; }
        public int CantDrivers { get; set; }
        public int CountRoutes { get; set; }
        public int Attemps { get; set; }
        public int Delivered { get; set; }
        public decimal OnTimePercent { get; set; }
        public string Status { get; set; } = "";
    }
}