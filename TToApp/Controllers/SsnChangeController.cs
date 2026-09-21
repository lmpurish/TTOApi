using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TToApp.DTOs;
using TToApp.Model;
using TToApp.Model.TToApp.Model;
using TToApp.Services.SsnVerification;
namespace TToApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SsnChangeController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IDataProtector _protector;
        private readonly ISsnDocumentVerificationService
    _ssnVerificationService;
        private readonly IWebHostEnvironment _environment;

        public SsnChangeController(
            ApplicationDbContext context,
            IDataProtectionProvider dataProtectionProvider,
            ISsnDocumentVerificationService ssnVerificationService,
            IWebHostEnvironment environment)
        {
            _context = context;

            _protector =
                dataProtectionProvider.CreateProtector(
                    "TToApp.SSN"
                );
            _ssnVerificationService = ssnVerificationService;
            _environment = environment;
        }


        // =====================================================
        // CURRENT USER ID
        // =====================================================

        private int? GetCurrentUserId()
        {
            var claim =
                User.FindFirst(
                    ClaimTypes.NameIdentifier
                );

            if (
                claim == null ||
                !int.TryParse(
                    claim.Value,
                    out var userId
                )
            )
            {
                return null;
            }

            return userId;
        }


        // =====================================================
        // 1. DRIVER CREATES REQUEST
        // POST api/SsnChange/request
        // =====================================================

        [HttpPost("request")]
        public async Task<IActionResult> CreateRequest(
            [FromBody] CreateSsnChangeRequestDto dto)
        {
            var userId =
                GetCurrentUserId();

            if (!userId.HasValue)
            {
                return Unauthorized();
            }


            var reason =
                dto.Reason?.Trim();

            if (
                string.IsNullOrWhiteSpace(reason) ||
                reason.Length < 10
            )
            {
                return BadRequest(new
                {
                    Message =
                        "Please provide a valid reason for the SSN change request."
                });
            }


            var user =
                await _context.Users
                    .Include(u => u.Profile)
                    .FirstOrDefaultAsync(
                        u => u.Id == userId.Value
                    );


            if (user == null)
            {
                return NotFound(new
                {
                    Message = "User not found."
                });
            }


            // User must already have an SSN.
            // Initial SSN registration belongs to onboarding.

            if (
                user.Profile == null ||
                string.IsNullOrWhiteSpace(
                    user.Profile.SsnEncrypted
                )
            )
            {
                return BadRequest(new
                {
                    Message =
                        "No Social Security number is currently registered."
                });
            }


            // Do not allow multiple active requests.

            var existingRequest =
                await _context.SsnChangeRequests
                    .Where(r =>
                        r.UserId == userId.Value &&
                        (
                            r.Status == "Pending" ||
                            r.Status == "Approved"
                        )
                    )
                    .OrderByDescending(
                        r => r.RequestedAt
                    )
                    .FirstOrDefaultAsync();


            if (existingRequest != null)
            {
                return Conflict(new
                {
                    Message =
                        "You already have an active SSN change request.",

                    RequestId =
                        existingRequest.Id,

                    Status =
                        existingRequest.Status
                });
            }


            var request =
                new SsnChangeRequest
                {
                    UserId =
                        userId.Value,

                    Reason =
                        reason,

                    Status =
                        "Pending",

                    RequestedAt =
                        DateTime.UtcNow
                };


            _context.SsnChangeRequests.Add(
                request
            );

            await _context.SaveChangesAsync();


            return Ok(new
            {
                Message =
                    "Your SSN change request has been submitted for staff review.",

                RequestId =
                    request.Id,

                Status =
                    request.Status
            });
        }


        // =====================================================
        // 2. DRIVER GETS CURRENT REQUEST STATUS
        // GET api/SsnChange/status
        // =====================================================

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId =
                GetCurrentUserId();

            if (!userId.HasValue)
            {
                return Unauthorized();
            }


            var request =
                await _context.SsnChangeRequests
                    .AsNoTracking()
                    .Where(
                        r => r.UserId == userId.Value
                    )
                    .OrderByDescending(
                        r => r.RequestedAt
                    )
                    .Select(r => new
                    {
                        r.Id,
                        r.Reason,
                        r.Status,
                        r.RequestedAt,
                        r.ReviewedAt,
                        r.StaffNotes,
                        r.AuthorizationExpiresAt,
                        r.CompletedAt
                    })
                    .FirstOrDefaultAsync();


            if (request == null)
            {
                return Ok(new
                {
                    HasRequest = false
                });
            }


            return Ok(new
            {
                HasRequest = true,

                Request = request
            });
        }


        // =====================================================
        // 3. STAFF LIST
        // GET api/SsnChange/staff/requests
        // =====================================================

        [Authorize(
            Roles =
                "Admin,CompanyOwner,Assistant,Manager"
        )]
        [HttpGet("staff/requests")]
        public async Task<IActionResult> GetRequests(
            [FromQuery] string? status = null)
        {
            var query =
                _context.SsnChangeRequests
                    .AsNoTracking()
                    .AsQueryable();


            if (
                !string.IsNullOrWhiteSpace(status)
            )
            {
                query =
                    query.Where(
                        r => r.Status == status
                    );
            }


            var requests =
                await query
                    .OrderByDescending(
                        r => r.RequestedAt
                    )
                    .Select(r => new
                    {
                        r.Id,

                        r.UserId,

                        DriverName =
                            r.User != null
                                ? (
                                    r.User.Name +
                                    " " +
                                    r.User.LastName
                                )
                                : "",

                        DriverEmail =
                            r.User != null
                                ? r.User.Email
                                : "",

                        r.Reason,

                        r.Status,

                        r.RequestedAt,

                        r.ReviewedAt,

                        r.StaffNotes,

                        r.AuthorizationExpiresAt,

                        r.CompletedAt
                    })
                    .ToListAsync();


            return Ok(requests);
        }


        // =====================================================
        // 4. STAFF APPROVES
        // POST api/SsnChange/staff/{id}/approve
        // =====================================================

        [Authorize(
            Roles =
                "Admin,CompanyOwner,Assistant,Manager"
        )]
        [HttpPost("staff/{id:long}/approve")]
        public async Task<IActionResult> Approve(
            long id,
            [FromBody] ReviewSsnChangeRequestDto dto)
        {
            var staffId =
                GetCurrentUserId();

            if (!staffId.HasValue)
            {
                return Unauthorized();
            }


            var request =
                await _context.SsnChangeRequests
                    .FirstOrDefaultAsync(
                        r => r.Id == id
                    );


            if (request == null)
            {
                return NotFound(new
                {
                    Message =
                        "SSN change request not found."
                });
            }


            if (
                request.Status != "Pending"
            )
            {
                return BadRequest(new
                {
                    Message =
                        $"This request cannot be approved because its current status is {request.Status}."
                });
            }


            var now =
                DateTime.UtcNow;


            request.Status =
                "Approved";

            request.ReviewedByUserId =
                staffId.Value;

            request.ReviewedAt =
                now;

            request.StaffNotes =
                dto.StaffNotes?.Trim();


            // Driver has 24 hours to perform
            // the authorized change.

            request.AuthorizationExpiresAt =
                now.AddHours(24);


            await _context.SaveChangesAsync();


            return Ok(new
            {
                Message =
                    "SSN change request approved.",

                request.Id,

                request.Status,

                request.AuthorizationExpiresAt
            });
        }


        // =====================================================
        // 5. STAFF REJECTS
        // POST api/SsnChange/staff/{id}/reject
        // =====================================================

        [Authorize(
            Roles =
                "Admin,CompanyOwner,Assistant,Manager"
        )]
        [HttpPost("staff/{id:long}/reject")]
        public async Task<IActionResult> Reject(
            long id,
            [FromBody] ReviewSsnChangeRequestDto dto)
        {
            var staffId =
                GetCurrentUserId();

            if (!staffId.HasValue)
            {
                return Unauthorized();
            }


            var request =
                await _context.SsnChangeRequests
                    .FirstOrDefaultAsync(
                        r => r.Id == id
                    );


            if (request == null)
            {
                return NotFound(new
                {
                    Message =
                        "SSN change request not found."
                });
            }


            if (
                request.Status != "Pending"
            )
            {
                return BadRequest(new
                {
                    Message =
                        $"This request cannot be rejected because its current status is {request.Status}."
                });
            }


            request.Status =
                "Rejected";

            request.ReviewedByUserId =
                staffId.Value;

            request.ReviewedAt =
                DateTime.UtcNow;

            request.StaffNotes =
                dto.StaffNotes?.Trim();

            request.AuthorizationExpiresAt =
                null;


            await _context.SaveChangesAsync();


            return Ok(new
            {
                Message =
                    "SSN change request rejected.",

                request.Id,

                request.Status
            });
        }


        // =====================================================
        // 6. DRIVER COMPLETES APPROVED CHANGE
        // POST api/SsnChange/{id}/complete
        // =====================================================

        [HttpPost("{id:long}/complete")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> Complete(
    long id,
    [FromForm] CompleteSsnChangeDto request,
    CancellationToken ct)
        {
            // =====================================================
            // CURRENT USER
            // =====================================================

            var userIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("id");


            if (
                userIdClaim == null ||
                !int.TryParse(
                    userIdClaim.Value,
                    out var userId
                )
            )
            {
                return Unauthorized(
                    new
                    {
                        Message =
                            "Invalid authentication token."
                    }
                );
            }


            // =====================================================
            // SSN CHANGE REQUEST
            //
            // IMPORTANT:
            // Request MUST belong to current user.
            // =====================================================

            var changeRequest =
                await _context
                    .SsnChangeRequests
                    .FirstOrDefaultAsync(
                        x =>
                            x.Id == id &&
                            x.UserId == userId,
                        ct
                    );


            if (changeRequest == null)
            {
                return NotFound(
                    new
                    {
                        Message =
                            "SSN change request not found."
                    }
                );
            }


            // =====================================================
            // REQUEST STATUS
            // =====================================================

            if (
                !string.Equals(
                    changeRequest.Status,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Conflict(
                    new
                    {
                        Message =
                            "This SSN change request is not approved."
                    }
                );
            }


            // =====================================================
            // AUTHORIZATION EXPIRATION
            // =====================================================

            if (
                !changeRequest
                    .AuthorizationExpiresAt
                    .HasValue
            )
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        Message =
                            "This SSN change request does not have a valid authorization."
                    }
                );
            }


            if (
                changeRequest
                    .AuthorizationExpiresAt
                    .Value <= DateTime.UtcNow
            )
            {
                changeRequest.Status =
                    "Expired";


                await _context
                    .SaveChangesAsync(ct);


                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        Message =
                            "This SSN change authorization has expired."
                    }
                );
            }


            // =====================================================
            // VALIDATE SSN
            // =====================================================

            if (
                string.IsNullOrWhiteSpace(
                    request.SocialSecurityNumber
                )
            )
            {
                return BadRequest(
                    new
                    {
                        Message =
                            "Social Security number is required."
                    }
                );
            }


            var ssn =
                request
                    .SocialSecurityNumber
                    .Replace("-", "")
                    .Replace(" ", "")
                    .Trim();


            if (
                ssn.Length != 9 ||
                !ssn.All(char.IsDigit)
            )
            {
                return BadRequest(
                    new
                    {
                        Message =
                            "Social Security number must contain exactly 9 digits."
                    }
                );
            }


            // =====================================================
            // DOCUMENT REQUIRED
            // =====================================================

            var file =
                request.SocialSecurityFile;


            if (
                file == null ||
                file.Length <= 0
            )
            {
                return BadRequest(
                    new
                    {
                        Message =
                            "Social Security card image is required."
                    }
                );
            }


            // =====================================================
            // FILE SIZE
            // =====================================================

            const long maxFileSize =
                10 * 1024 * 1024;


            if (
                file.Length > maxFileSize
            )
            {
                return BadRequest(
                    new
                    {
                        Message =
                            "The Social Security card image cannot exceed 10 MB."
                    }
                );
            }


            // =====================================================
            // ALLOWED CONTENT TYPES
            // =====================================================

            var allowedContentTypes =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                )
                {
            "image/jpeg",
            "image/png",
            "image/webp"
                };


            if (
                string.IsNullOrWhiteSpace(
                    file.ContentType
                ) ||
                !allowedContentTypes.Contains(
                    file.ContentType
                )
            )
            {
                return BadRequest(
                    new
                    {
                        Message =
                            "Only JPG, PNG, and WEBP images are allowed."
                    }
                );
            }


            // =====================================================
            // USER
            // =====================================================

            var user =
                await _context
                    .Users
                    .Include(x => x.Profile)
                    .FirstOrDefaultAsync(
                        x => x.Id == userId,
                        ct
                    );


            if (
                user == null
            )
            {
                return NotFound(
                    new
                    {
                        Message =
                            "User not found."
                    }
                );
            }


            if (
                user.Profile == null
            )
            {
                return BadRequest(
                    new
                    {
                        Message =
                            "User profile not found."
                    }
                );
            }


            // =====================================================
            // VERIFY NAME ON DOCUMENT
            // =====================================================

            SsnDocumentVerificationResult
                verification;


            try
            {
                verification =
                    await _ssnVerificationService
                        .VerifyAsync(
                            file,
                            user.Name ?? string.Empty,
                            user.LastName ?? string.Empty,
                            ct
                        );
            }
            catch
            {
                return StatusCode(
                    StatusCodes
                        .Status503ServiceUnavailable,
                    new
                    {
                        Message =
                            "The document verification service is temporarily unavailable."
                    }
                );
            }


            // =====================================================
            // STORE SAFE VERIFICATION METADATA
            // =====================================================

            changeRequest
                .DocumentVerificationStatus =
                verification.Status;


            changeRequest
                .DocumentVerificationConfidence =
                verification.Confidence;


            changeRequest
                .DocumentVerifiedAt =
                DateTime.UtcNow;


            changeRequest
                .DocumentVerificationProvider =
                verification.Provider;


            changeRequest
                .DocumentVerificationNotes =
                verification.FailureReason;


            // =====================================================
            // AUTOMATIC VERIFICATION FAILED
            //
            // DO NOT CHANGE SSN.
            // =====================================================

            if (
                !verification.Success ||
                !verification.NameMatched
            )
            {
                changeRequest.Status =
                    "NeedsReview";


                await _context
                    .SaveChangesAsync(ct);


                return UnprocessableEntity(
                    new
                    {
                        Message =
                            "We could not automatically verify that the name on the Social Security card matches your account. The request has been sent for staff review.",

                        Status =
                            "NeedsReview"
                    }
                );
            }


            // =====================================================
            // ENCRYPTION SERVICE
            // =====================================================

            if (
                _protector == null
            )
            {
                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    new
                    {
                        Message =
                            "Encryption service is not available."
                    }
                );
            }


            // =====================================================
            // ENCRYPT NEW SSN
            // =====================================================

            string encryptedSsn;


            try
            {
                encryptedSsn =
                    _protector.Protect(
                        ssn
                    );
            }
            catch
            {
                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    new
                    {
                        Message =
                            "Unable to securely process the Social Security number."
                    }
                );
            }


            if (
                string.IsNullOrWhiteSpace(
                    encryptedSsn
                )
            )
            {
                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    new
                    {
                        Message =
                            "Unable to securely process the Social Security number."
                    }
                );
            }


            // =====================================================
            // SAVE DOCUMENT PRIVATELY
            // =====================================================

            string documentPath;


            try
            {
                documentPath =
                    await SavePrivateAsync(
                        file,
                        "socialSecurities"
                    );
            }
            catch
            {
                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    new
                    {
                        Message =
                            "Unable to securely save the Social Security document."
                    }
                );
            }


            // =====================================================
            // UPDATE PROFILE
            // =====================================================

            user.Profile.SsnEncrypted =
                encryptedSsn;


            user.Profile.SsnLast4 =
                ssn[^4..];


            user.Profile.SsnUpdatedAt =
                DateTime.UtcNow;


            user.Profile.SocialSecurityUrl =
                documentPath;


            // =====================================================
            // COMPLETE REQUEST
            // =====================================================

            changeRequest.Status =
                "Completed";


            changeRequest.CompletedAt =
                DateTime.UtcNow;


            changeRequest
                .DocumentVerificationStatus =
                "Matched";


            // =====================================================
            // SAVE EVERYTHING
            // =====================================================

            await _context
                .SaveChangesAsync(ct);


            // =====================================================
            // IMPORTANT:
            // NEVER RETURN SSN.
            // =====================================================

            return Ok(
                new
                {
                    Message =
                        "Your Social Security number has been updated successfully.",

                    Status =
                        "Completed",

                    SocialSecurity =
                        new
                        {
                            Masked =
                                $"***-**-{user.Profile.SsnLast4}"
                        }
                }
            );
        }

        private async Task<string> SavePrivateAsync(
    IFormFile file,
    string folder)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException(
                    "File is empty.",
                    nameof(file)
                );
            }

            // =====================================================
            // PRIVATE STORAGE ROOT
            // =====================================================

            var privateRoot = Path.Combine(
                _environment.ContentRootPath,
                "PrivateUploads",
                folder
            );

            Directory.CreateDirectory(privateRoot);


            // =====================================================
            // SAFE EXTENSION
            // =====================================================

            var extension = Path
                .GetExtension(file.FileName)
                .ToLowerInvariant();

            var allowedExtensions = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            )
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

            if (!allowedExtensions.Contains(extension))
            {
                throw new InvalidOperationException(
                    "Invalid file extension."
                );
            }


            // =====================================================
            // RANDOM FILE NAME
            // =====================================================

            var fileName =
                $"{Guid.NewGuid():N}{extension}";


            var fullPath = Path.Combine(
                privateRoot,
                fileName
            );


            // =====================================================
            // SAVE
            // =====================================================

            await using (
                var stream = new FileStream(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                await file.CopyToAsync(stream);
            }


            // =====================================================
            // IMPORTANT
            //
            // Return only internal relative path.
            // Do NOT return physical server path.
            // =====================================================

            return Path.Combine(
                    "PrivateUploads",
                    folder,
                    fileName
                )
                .Replace("\\", "/");
        }
    }
}