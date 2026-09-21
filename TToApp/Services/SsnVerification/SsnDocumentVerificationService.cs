using Microsoft.AspNetCore.Http;

namespace TToApp.Services.SsnVerification;

public sealed class SsnDocumentVerificationService
    : ISsnDocumentVerificationService
{
    public Task<SsnDocumentVerificationResult>
        VerifyAsync(
            IFormFile file,
            string firstName,
            string lastName,
            CancellationToken ct = default
        )
    {
        // =================================================
        // IMPORTANT
        // =================================================
        //
        // OCR provider will be connected here.
        //
        // For now we DO NOT pretend that the document
        // has been verified.
        //
        // The result is sent to manual review.
        // =================================================

        var result =
            new SsnDocumentVerificationResult
            {
                Success = true,

                NameMatched = false,

                Confidence = 0,

                Status = "NeedsReview",

                Provider = "ManualReview",

                FailureReason =
                    "Automatic document verification is not configured."
            };


        return Task.FromResult(
            result
        );
    }
}