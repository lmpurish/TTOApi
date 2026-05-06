using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;
using TToApp.Services.Vehicle;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin,CompanyOwner")]
    public class RentalVehiclesController : ControllerBase
    {
      


       
            private readonly IVehicleService _vehicleService;
            private readonly IWebHostEnvironment _env;
            private readonly ApplicationDbContext _context;

        public RentalVehiclesController(IVehicleService vehicleService, IWebHostEnvironment env, ApplicationDbContext context)
            {
                _vehicleService = vehicleService;
                _env = env;
             _context= context;
            }

        [HttpGet]
        public async Task<IActionResult> GetVehicles(
[FromQuery] int? metroId,
[FromQuery] string? status)
        {
            var companyIdResult = GetCurrentCompanyId();

            if (!companyIdResult.Success)
                return Unauthorized(new { message = companyIdResult.Message });

            var query = _context.RentalVehicles
                .AsNoTracking()
                .Where(v => v.IsActive && v.CompanyId == companyIdResult.CompanyId);

            if (metroId.HasValue)
                query = query.Where(v => v.MetroId == metroId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(v => v.Status == status);

            var vehicles = await query
                .OrderByDescending(v => v.CreatedAt)
                .Select(v => new
                {
                    v.Id,
                    v.DisplayName,
                    v.CompanyId,
                    v.MetroId,
                    v.StockNumber,
                    v.Trim,
                    v.Color,
                    v.Transmission,
                    v.FuelType,
                    v.Vin,
                    v.Plate,
                    v.FacilityLocation,
                    v.SeatingCapacity,
                    v.GpsInstalled,
                    v.DashCamInstalled,
                    v.Year,
                    v.Make,
                    v.Model,
                    v.Status,
                    v.DailyPrice,
                    v.WeeklyPrice,

                    MainImageUrl = _context.VehicleImages
                        .Where(i => i.VehicleId == v.Id)
                        .OrderByDescending(i => i.IsCover)
                        .ThenBy(i => i.SortOrder)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),

                    ImagesCount = _context.VehicleImages.Count(i => i.VehicleId == v.Id)
                })
                .ToListAsync();

            return Ok(vehicles);
        }

        [HttpGet("{id:int}")]
            public async Task<IActionResult> GetVehicle(int id)
            {
                var companyIdResult = GetCurrentCompanyId();
                if (!companyIdResult.Success)
                    return Unauthorized(new { message = companyIdResult.Message });

                var vehicle = await _vehicleService.GetVehicleByIdAsync(id);

                if (vehicle == null || !vehicle.IsActive)
                    return NotFound(new { message = "Vehicle not found." });

                if (vehicle.CompanyId != companyIdResult.CompanyId)
                    return Forbid();

                return Ok(vehicle);
            }

        [HttpPost]
        public async Task<IActionResult> CreateVehicle([FromForm] CreateVehicleDto dto)
        {
            var companyIdResult = GetCurrentCompanyId();

            if (!companyIdResult.Success)
                return Unauthorized(new { message = companyIdResult.Message });

            dto.CompanyId = companyIdResult.CompanyId;

            var result = await _vehicleService.CreateVehicleAsync(dto);

            if (!result.Success || result.Vehicle == null)
                return BadRequest(new { message = result.Message });

            var vehicle = result.Vehicle;

            if (dto.Images != null && dto.Images.Any())
            {
                var folder = Path.Combine(
                    _env.WebRootPath,
                    "storage",
                    "vehicles",
                    vehicle.Id.ToString()
                );

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                int order = 0;

                foreach (var file in dto.Images)
                {
                    if (file.Length == 0)
                        continue;

                    if (!file.ContentType.StartsWith("image/"))
                        continue;

                    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                    var path = Path.Combine(folder, fileName);

                    await using var stream = new FileStream(path, FileMode.Create);
                    await file.CopyToAsync(stream);

                    var image = new VehicleImage
                    {
                        VehicleId = vehicle.Id,
                        CompanyId = vehicle.CompanyId,
                        FileName = fileName,
                        ImageUrl = $"/storage/vehicles/{vehicle.Id}/{fileName}",
                        IsCover = order == 0,
                        SortOrder = order,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.VehicleImages.Add(image);
                    order++;
                }

                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                message = result.Message,
                vehicle = result.Vehicle
            });
        }

        [HttpPut("{id:int}")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateVehicle(int id, [FromForm] UpdateVehicleDto dto)
        {
            var companyIdResult = GetCurrentCompanyId();
            if (!companyIdResult.Success)
                return Unauthorized(new { message = companyIdResult.Message });

            var existingVehicle = await _vehicleService.GetVehicleByIdAsync(id);

            if (existingVehicle == null || !existingVehicle.IsActive)
                return NotFound(new { message = "Vehicle not found." });

            if (existingVehicle.CompanyId != companyIdResult.CompanyId)
                return Forbid();

            dto.CompanyId = companyIdResult.CompanyId;

            var result = await _vehicleService.UpdateVehicleAsync(id, dto);

            if (!result.Success)
                return BadRequest(new { message = result.Message });

            return Ok(new
            {
                message = result.Message,
                vehicle = result.Vehicle
            });
        }

        [HttpPatch("{id:int}/status")]
            public async Task<IActionResult> UpdateVehicleStatus(int id, [FromBody] UpdateVehicleStatusDto dto)
            {
                var companyIdResult = GetCurrentCompanyId();
                if (!companyIdResult.Success)
                    return Unauthorized(new { message = companyIdResult.Message });

                var existingVehicle = await _vehicleService.GetVehicleByIdAsync(id);

                if (existingVehicle == null || !existingVehicle.IsActive)
                    return NotFound(new { message = "Vehicle not found." });

                if (existingVehicle.CompanyId != companyIdResult.CompanyId)
                    return Forbid();

                var result = await _vehicleService.UpdateVehicleStatusAsync(id, dto.Status);

                if (!result.Success)
                    return BadRequest(new { message = result.Message });

                return Ok(new { message = result.Message });
            }

            [HttpDelete("{id:int}")]
            public async Task<IActionResult> ArchiveVehicle(int id)
            {
                var companyIdResult = GetCurrentCompanyId();
                if (!companyIdResult.Success)
                    return Unauthorized(new { message = companyIdResult.Message });

                var existingVehicle = await _vehicleService.GetVehicleByIdAsync(id);

                if (existingVehicle == null || !existingVehicle.IsActive)
                    return NotFound(new { message = "Vehicle not found." });

                if (existingVehicle.CompanyId != companyIdResult.CompanyId)
                    return Forbid();

                var result = await _vehicleService.ArchiveVehicleAsync(id);

                if (!result.Success)
                    return BadRequest(new { message = result.Message });

                return Ok(new { message = result.Message });
            }

            private (bool Success, int CompanyId, string Message) GetCurrentCompanyId()
            {
                var companyIdClaim = User.FindFirst("CompanyId")?.Value;

                if (string.IsNullOrWhiteSpace(companyIdClaim))
                    return (false, 0, "Company context not found in token.");

                if (!int.TryParse(companyIdClaim, out var companyId))
                    return (false, 0, "Invalid company context in token.");

                return (true, companyId, string.Empty);
            }

            
        }
    
}

