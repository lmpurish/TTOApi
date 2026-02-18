namespace TToApp.Model
{
    public class LoanRepayment
    {
        public long Id { get; set; }

        public long LoanId { get; set; }
        public long? PayRunId { get; set; }
        public long DriverId { get; set; }

        public decimal Amount { get; set; } // positivo
        public string Status { get; set; } = "Applied"; // Applied|Reversed

        public long AppliedBy { get; set; }
        public DateTime AppliedAt { get; set; }

        public long? ReversedBy { get; set; }
        public DateTime? ReversedAt { get; set; }
        public string? Reason { get; set; }

        public EmployeeLoan Loan { get; set; } = null!;
        public PayRun? PayRun { get; set; } = null!;
    }
}
