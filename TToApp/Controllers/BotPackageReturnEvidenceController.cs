using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/bot/package-return-evidence")]
    [ApiController]
    public class BotPackageReturnEvidenceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public BotPackageReturnEvidenceController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            IConfiguration config)
        {
            _context = context;
            _env = env;
            _config = config;
        }

        [HttpPost]
        public async Task<IActionResult> CreateEvidence([FromBody] CreatePackageReturnEvidenceDto dto)
        {
         /*   var apiKey = Request.Headers["X-Bot-Api-Key"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _config["BotApiKey"])
            {
                return Unauthorized(new { message = "Invalid bot API key." });
            }*/
            if (string.IsNullOrWhiteSpace(dto.Tracking))
                return BadRequest(new { message = "Tracking is required." });

            if (string.IsNullOrWhiteSpace(dto.ImageBase64) && string.IsNullOrWhiteSpace(dto.ImageUrl))
                return BadRequest(new { message = "ImageBase64 or ImageUrl is required." });

            var tracking = dto.Tracking.Trim();

            var package = await _context.Packages
                .FirstOrDefaultAsync(p => p.Tracking == tracking);

            User? driver = null;

            if (dto.DriverId.HasValue)
            {
                driver = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == dto.DriverId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(dto.DriverPhone))
            {
                var phone = NormalizePhone(dto.DriverPhone);

                var drivers = await _context.Users
                    .Include(u => u.Profile)
                    .Where(u =>
                        u.UserRole == global::User.Role.Driver &&
                        u.Profile != null &&
                        u.Profile.PhoneNumber != null)
                    .ToListAsync();

                driver = drivers.FirstOrDefault(u =>
                    NormalizePhone(u.Profile!.PhoneNumber) == phone);
            }

            string imageUrl;

            if (!string.IsNullOrWhiteSpace(dto.ImageBase64))
            {
                imageUrl = await SaveBase64Image(dto.ImageBase64, tracking);
            }
            else
            {
                imageUrl = dto.ImageUrl!;
            }

            var evidence = new PackageReturnEvidence
            {
                Tracking = tracking,
                PackageId = package?.Id,
                DriverId = driver?.Id ?? dto.DriverId,
                WarehouseId = dto.WarehouseId ?? driver?.WarehouseId,
                DriverPhone = dto.DriverPhone,
                DriverName = dto.DriverName ?? $"{driver?.Name} {driver?.LastName}".Trim(),
                Reason = dto.Reason,
                Message = dto.Message,
                ImageUrl = imageUrl,
                Source = "WhatsAppBot",
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.PackageReturnEvidences.Add(evidence);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                evidenceId = evidence.Id,
                packageFound = package != null,
                driverFound = driver != null,
                evidence
            });
        }
        private static string NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return string.Empty;

            var digits = new string(phone.Where(char.IsDigit).ToArray());

            if (digits.Length == 11 && digits.StartsWith("1"))
                digits = digits.Substring(1);

            return digits;
        }
        private async Task<string> SaveBase64Image(string base64, string tracking)
        {
            if (base64.Contains(","))
                base64 = base64.Split(",")[1];

            var bytes = Convert.FromBase64String(base64);
            var webRoot = _env.WebRootPath;

            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            }

            var folder = Path.Combine(webRoot, "uploads", "package-evidence");
            Console.WriteLine("ENTRO A SaveBase64Image");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var fileName = $"{tracking}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}.jpg";
            var filePath = Path.Combine(folder, fileName);

            await System.IO.File.WriteAllBytesAsync(filePath, bytes);

            return filePath;
        }
    }
}
