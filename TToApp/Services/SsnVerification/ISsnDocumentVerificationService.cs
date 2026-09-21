using Microsoft.AspNetCore.Http;

namespace TToApp.Services.SsnVerification;

public interface ISsnDocumentVerificationService
{
    Task<SsnDocumentVerificationResult>
        VerifyAsync(
            IFormFile file,
            string firstName,
            string lastName,
            CancellationToken ct = default
        );
}