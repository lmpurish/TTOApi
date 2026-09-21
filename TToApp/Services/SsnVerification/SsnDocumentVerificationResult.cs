namespace TToApp.Services.SsnVerification
{
    public sealed class SsnDocumentVerificationResult
    {
        public bool Success { get; set; }

        public bool NameMatched { get; set; }

        public decimal Confidence { get; set; }

        public string Status { get; set; }
            = "NeedsReview";

        public string Provider { get; set; }
            = string.Empty;

        public string? FailureReason { get; set; }
    }
}
