using System.ComponentModel.DataAnnotations;

namespace TToApp.DTOs
{
    public sealed class CreateSsnChangeRequestDto
    {
        [Required]
        [StringLength(1000, MinimumLength = 10)]
        public string Reason { get; set; } = string.Empty;
    }


    public sealed class ReviewSsnChangeRequestDto
    {
        [StringLength(1000)]
        public string? StaffNotes { get; set; }
    }


    public sealed class CompleteSsnChangeDto
    {
        [Required]
        public string SocialSecurityNumber { get; set; } = string.Empty;

        [Required]
        public IFormFile? SocialSecurityFile { get; set; }
    }

}