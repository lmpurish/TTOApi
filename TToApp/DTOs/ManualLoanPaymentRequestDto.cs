namespace TToApp.DTOs
{
    public class ManualLoanPaymentRequestDto
    {
        public decimal Amount { get; set; }
        public string? Reason { get; set; } // "cash", "zelle", etc
        public DateTime? PaidAtUtc { get; set; } // opcional
    }
}
