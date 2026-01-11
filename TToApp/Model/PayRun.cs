namespace TToApp.Model
{
    public class PayRun
    {

        public long Id { get; set; }
        public long PayPeriodId { get; set; }
        public long DriverId { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal Adjustments { get; set; }
        public decimal NetAmount { get; private set; } // Computada
        public string Status { get; set; } = "Draft";   // Draft|Approved (sugerido)
        public DateTime? CalculatedAt { get; set; }
        public long? CalculatedBy { get; set; }

        public PayPeriod PayPeriod { get; set; } = null!;
        public ICollection<PayRunLine> Lines { get; set; } = new List<PayRunLine>();
        public ICollection<PayrollAdjustment> AdjustmentsList { get; set; } = new List<PayrollAdjustment>();
    }
}
