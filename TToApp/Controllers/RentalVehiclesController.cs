using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TToApp.DTOs;
using TToApp.Services.Vehicle;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin,CompanyOwner")]
    public class RentalVehiclesController : ControllerBase
    {
      


       
            private readonly IVehicleService _vehicleService;

            public RentalVehiclesController(IVehicleService vehicleService)
            {
                _vehicleService = vehicleService;
            }

            [HttpGet]
            public async Task<IActionResult> GetVehicles(
                [FromQuery] int? metroId,
                [FromQuery] string? status)
            {
                var companyIdResult = GetCurrentCompanyId();
                if (!companyIdResult.Success)
                    return Unauthorized(new { message = companyIdResult.Message });

                var vehicles = await _vehicleService.GetVehiclesAsync(companyIdResult.CompanyId, metroId, status);
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
            public async Task<IActionResult> CreateVehicle([FromBody] CreateVehicleDto dto)
            {
                var companyIdResult = GetCurrentCompanyId();
                if (!companyIdResult.Success)
                    return Unauthorized(new { message = companyIdResult.Message });

                // Fuerza la compañía del usuario autenticado
                dto.CompanyId = companyIdResult.CompanyId;

                var result = await _vehicleService.CreateVehicleAsync(dto);

                if (!result.Success)
                    return BadRequest(new { message = result.Message });

                return Ok(new
                {
                    message = result.Message,
                    vehicle = result.Vehicle
                });
            }

            [HttpPut("{id:int}")]
            public async Task<IActionResult> UpdateVehicle(int id, [FromBody] UpdateVehicleDto dto)
            {
                var companyIdResult = GetCurrentCompanyId();
                if (!companyIdResult.Success)
                    return Unauthorized(new { message = companyIdResult.Message });

                var existingVehicle = await _vehicleService.GetVehicleByIdAsync(id);

                if (existingVehicle == null || !existingVehicle.IsActive)
                    return NotFound(new { message = "Vehicle not found." });

                if (existingVehicle.CompanyId != companyIdResult.CompanyId)
                    return Forbid();

                // Fuerza la compañía correcta
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

