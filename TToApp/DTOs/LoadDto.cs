namespace TToApp.DTOs
{
    public class LoanDto
    {
        public long Id { get; set; }
        public long DriverId { get; set; }
        public decimal Principal { get; set; }
        public decimal Balance { get; set; }
        public decimal? InstallmentAmount { get; set; }
        public decimal? MaxDeductionPerPayRun { get; set; }
        public string Status { get; set; } = null!;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }
}
