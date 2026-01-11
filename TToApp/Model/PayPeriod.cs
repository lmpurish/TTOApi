using Stripe;

namespace TToApp.Model
{
    public sealed class PayPeriod
    {
        public long Id { get; set; }
        public long CompanyId { get; set; }
        public long? WarehouseId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public string Status { get; set; } = "Open"; // Open|Locked|Approved (sugerido)
        public string? Notes { get; set; }
        public long CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<PayRun> PayRuns { get; set; } = new List<PayRun>();
    }
}
