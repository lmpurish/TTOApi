namespace TToApp.Model
{
    public class EmployeeLoan
    {
        public long Id { get; set; }
        public int DriverId { get; set; }
        public User? Driver {  get; set; }

        public decimal Principal { get; set; }
        public decimal Balance { get; set; }

        public decimal? InstallmentAmount { get; set; }     // cuota fija
        public decimal? MaxDeductionPerPayRun { get; set; } // tope por payroll

        public string Status { get; set; } = "Draft"; // Draft|Active|Paused|Completed|Cancelled
        public string? Notes { get; set; }

        public int CreatedBy { get; set; }
        public User? CreatedByUser { get; set; }
        public DateTime CreatedAt { get; set; }

        public int? ApprovedBy { get; set; }
        public User? ApprovedByUser { get; set; }   // 👈 agrega esto
        public DateTime? ApprovedAt { get; set; }

        public ICollection<LoanRepayment> Repayments { get; set; } = new List<LoanRepayment>();
    }
}
