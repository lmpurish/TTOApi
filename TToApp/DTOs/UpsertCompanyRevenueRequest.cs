namespace TToApp.DTOs
{
    public class UpsertCompanyRevenueRequest
    {
        public long PayPeriodId { get; set; }

        public int WarehouseId { get; set; }

        public decimal Revenue { get; set; }

        public decimal Expenses { get; set; }

        public decimal Adjustments { get; set; }

        public string RevenueType { get; set; } = "Settlement";

        public string? Notes { get; set; }

        public DateTime? RevenueDate { get; set; }
    }
}
