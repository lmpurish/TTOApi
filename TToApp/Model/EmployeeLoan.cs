namespace TToApp.Model
{
    public class EmployeeLoan
    {
        public long Id { get; set; }
        public long DriverId { get; set; }

        public decimal Principal { get; set; }
        public decimal Balance { get; set; }

        public decimal? InstallmentAmount { get; set; }     // cuota fija
        public decimal? MaxDeductionPerPayRun { get; set; } // tope por payroll

        public string Status { get; set; } = "Draft"; // Draft|Active|Paused|Completed|Cancelled
        public string? Notes { get; set; }

        public long CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        public long? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }

        public ICollection<LoanRepayment> Repayments { get; set; } = new List<LoanRepayment>();
    }
}
