namespace TToApp.DTOs
{
    public class CreateLoanRequestDto
    {
        public long DriverId { get; set; }
        public decimal Principal { get; set; }
        public decimal? InstallmentAmount { get; set; }
        public decimal? MaxDeductionPerPayRun { get; set; }
        public string? Notes { get; set; }
    }
}
