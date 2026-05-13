using System.ComponentModel.DataAnnotations;
using TToApp.Model;

namespace TToApp.DTOs
{
    public class WarehousePerformanceDto
    {
        public int WarehouseId { get; set; }
        public string Warehouse { get; set; } = "";
        public string City { get; set; } = "";
        public int Packages { get; set; }
        public int Delivered { get; set; }
        public int Drivers { get; set; }
        public int Routes { get; set; }
        public decimal OnTimePercent { get; set; }
        public string Status { get; set; } = "";
    }
}