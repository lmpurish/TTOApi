using System.Text.Json.Serialization;

namespace TToApp.Model
{
    public class PayrollAdjustment
    {
        public long Id { get; set; }
        public long PayRunId { get; set; }
        public string Type { get; set; } = null!; // Bonus|Penalty|Reimbursement
        public string? Reason { get; set; }
        public decimal Amount { get; set; }
        public long CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        [JsonIgnore]
        public PayRun PayRun { get; set; } = null!;
    }
}
