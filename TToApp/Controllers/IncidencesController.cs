using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class IncidencesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly IWebHostEnvironment _env;

        public IncidencesController(ApplicationDbContext context, EmailService emailService, IWebHostEnvironment env)
        {
            _context = context;
            _emailService = emailService;
            _env = env;
        }

        // POST: api/Incidences/uploadImage
        [HttpPost("uploadImage")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded." });

            const long maxBytes = 5 * 1024 * 1024; // 5 MB
            if (file.Length > maxBytes)
                return BadRequest(new { message = "File size exceeds the 5 MB limit." });

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                return BadRequest(new { message = "Unsupported file type. Use jpg, png, gif or webp." });

            var folderPath = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "storage", "incidences");
            Directory.CreateDirectory(folderPath);

            var fileName = $"inc_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(folderPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            var imageUrl = $"{Request.Scheme}://{Request.Host}/storage/incidences/{fileName}";
            return Ok(new { imageName = fileName, imageUrl });
        }

        // GET: api/Incidences/byRoute/5
        [HttpGet("byRoute/{routeId}")]
        public async Task<ActionResult<IEnumerable<IncidenceResponseDto>>> GetByRoute(int routeId)
        {
            var route = await _context.Routes.FindAsync(routeId);
            if (route == null)
                return NotFound(new { message = "Route not found." });

            var incidences = await _context.Incidences
                .Where(i => i.RouteId == routeId)
                .OrderByDescending(i => i.OccurredAt)
                .Select(i => new IncidenceResponseDto
                {
                    Id = i.Id,
                    RouteId = i.RouteId,
                    UserId = i.UserId,
                    Type = i.Type.ToString(),
                    Description = i.Description,
                    ImageName = i.ImageName,
                    ImageUrl = i.ImageUrl,
                    OccurredAt = i.OccurredAt,
                    CreatedAt = i.CreatedAt
                })
                .ToListAsync();

            return Ok(incidences);
        }

        // POST: api/Incidences/addIncidences
        [HttpPost("addIncidences")]
        public async Task<ActionResult<IncidenceResponseDto>> AddIncidences([FromBody] AddIncidenceDto dto)
        {
            if (dto == null)
                return BadRequest(new { message = "Invalid data." });

            if (!Enum.TryParse<IncidenceType>(dto.Type, true, out var parsedType))
                return BadRequest(new { message = $"Invalid incidence type: '{dto.Type}'. Valid values: {string.Join(", ", Enum.GetNames<IncidenceType>())}" });

            var route = await _context.Routes.FindAsync(dto.RouteId);
            if (route == null)
                return NotFound(new { message = "Route not found." });

            var user = await _context.Users.FindAsync(dto.UserId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            try
            {
                var incidence = new Incidence
                {
                    RouteId = dto.RouteId,
                    UserId = dto.UserId,
                    Type = parsedType,
                    Description = dto.Description,
                    ImageName = dto.ImageName,
                    ImageUrl = dto.ImageUrl,
                    OccurredAt = dto.OccurredAt,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Incidences.Add(incidence);
                await _context.SaveChangesAsync();

                var imageBlock = string.IsNullOrEmpty(incidence.ImageUrl)
                    ? ""
                    : $"<div class=\"image-wrap\"><p><strong>📷 Attached photo:</strong></p><img src=\"{incidence.ImageUrl}\" alt=\"Incidence photo\" style=\"max-width:100%;border-radius:8px;border:1px solid #eee;\" /></div>";

                var placeholders = new Dictionary<string, string>
                {
                    ["Name"] = user.Name,
                    ["RouteId"] = incidence.RouteId.ToString(),
                    ["Type"] = incidence.Type.ToString(),
                    ["Description"] = incidence.Description ?? "-",
                    ["OccurredAt"] = incidence.OccurredAt.ToString("yyyy-MM-dd HH:mm"),
                    ["ImageBlock"] = imageBlock
                };

                await _emailService.SendEmailAsync(
                    toEmail: user.Email,
                    subject: $"New incidence reported on route #{incidence.RouteId}",
                    templateFileName: "IncidenceNotification.cshtml",
                    placeholders: placeholders
                );

                return Ok(new IncidenceResponseDto
                {
                    Id = incidence.Id,
                    RouteId = incidence.RouteId,
                    UserId = incidence.UserId,
                    Type = incidence.Type.ToString(),
                    Description = incidence.Description,
                    ImageName = incidence.ImageName,
                    ImageUrl = incidence.ImageUrl,
                    OccurredAt = incidence.OccurredAt,
                    CreatedAt = incidence.CreatedAt
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving incidence: {ex}");
                return StatusCode(500, new { message = "Internal server error." });
            }
        }
    }
}
