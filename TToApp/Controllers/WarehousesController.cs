using AutoMapper;
using Humanizer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WarehousesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly IApplicantContactService _applicantContactService;
        private readonly ILogger<WarehousesController> _logger;

        public WarehousesController(ApplicationDbContext context, IMapper mapper, IApplicantContactService applicantContact, ILogger<WarehousesController> logger)
        {
            _context = context;
            _mapper = mapper;
            _applicantContactService = applicantContact;
            _logger = logger;
        }

        // GET: api/Warehouses
        [Authorize]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<WarehouseDto>>> GetWarehouses()
        {
            try
            {
                var userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User not authenticated");

                var user = await _context.Users
                    .Include(u => u.Company)
                    .FirstOrDefaultAsync(u => u.Id == int.Parse(userId));

                if (user == null)
                    return Unauthorized("User not found");

                if (user.UserRole != global::User.Role.Admin && user.UserRole != global::User.Role.CompanyOwner && user.UserRole != global::User.Role.Manager && user.UserRole != global::User.Role.Assistant && user.UserRole.Value != global::User.Role.Recruiter)
                    return StatusCode(403, "Access denied. Only Company Owner and Admins can access this resource.");

                IQueryable<Warehouse> query;

                if (user.UserRole == global::User.Role.CompanyOwner)
                {
                    query = _context.Warehouses
                        .Include(w => w.Companie)
                        .Where(w => w.Companie != null && w.Companie.OwnerId == user.Id);
                }
                else // Admin
                {
                    if (!user.CompanyId.HasValue)
                        return StatusCode(403, "Admin does not belong to any company.");

                    query = _context.Warehouses
                        .Include(w => w.Companie)
                        .Where(w => w.CompanyId == user.CompanyId.Value);
                }



                // ✅ Proyectar a DTO (evita ciclos)
                var warehouses = await query
                    .Select(w => new WarehouseDto
                    {
                        Id = w.Id,
                        City = w.City,
                        Company = w.Company,
                        Address = w.Address,
                        State = w.State,
                        isHiring = w.IsHiring,
                        SendPayroll = w.SendPayroll,
                        CompanyId = (int)w.CompanyId,
                        ZipCode = w.ZipCode,
                        DriveRate = w.DriveRate,
                        OpenTime = w.OpenTime.HasValue
            ? new TimeDto { Hours = w.OpenTime.Value.Hour, Minutes = w.OpenTime.Value.Minute }
            : null,
                        AuthorizedPersons = _context.Permits
                        .Where(ap => ap.WarehouseId == w.Id)
                        .Select(ap => new AuthorizedPersonDto
                        {
                            Id = ap.UserId,
                            Name = ap.User.Name,
                            LastName = ap.User.LastName
                        })
                        .ToList(),
                        Metro = w.Metro == null
                       
                       
    ? null
    : new Metro
    {
        Id = w.Metro.Id,
        City = w.Metro.City
    },
                        Manager = _context.Users
                    .Where(u => u.WarehouseId == w.Id && u.UserRole == global::User.Role.Manager)
                    .Select(u => u.Name + " " + u.LastName)
                    .FirstOrDefault() ?? ""
                    })
                    .ToListAsync();

                return Ok(warehouses);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }

        private static TimeOnly? ToTimeOnly(TimeDto? t)
        {
            if (t is null) return null;
            if (t.Hours is < 0 or > 23 || t.Minutes is < 0 or > 59) return null;
            return new TimeOnly(t.Hours, t.Minutes);
        }

        private static TimeDto? FromTimeOnly(TimeOnly? t) =>
            t is null ? null : new TimeDto { Hours = t.Value.Hour, Minutes = t.Value.Minute };

        [AllowAnonymous] // O quita esto si quieres protegerlo
        [HttpGet("by-company/{companyId:int}")]
        public async Task<ActionResult<IEnumerable<WarehouseDto>>> GetWarehousesByCompanyId(int companyId, CancellationToken ct)
        {
            if (companyId <= 0)
                return BadRequest("Invalid companyId.");

            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .Where(w => w.CompanyId == companyId)
                .Select(w => new WarehouseDto
                {
                    Id = w.Id,
                    City = w.City,
                    Address = w.Address,
                    State = w.State,
                    Company = w.Companie != null ? w.Companie.Name : null,
                    isHiring = w.IsHiring,
                    SendPayroll = w.SendPayroll
                })
                .ToListAsync(ct);

            return Ok(warehouses);
        }




        [Authorize]
        [HttpGet("with-rsp")]
        public async Task<ActionResult<IEnumerable<WarehouseWithRspDto>>> GetWarehousesWithRsp()
        {
            var userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (userRole != global::User.Role.Admin.ToString() && userRole != global::User.Role.Manager.ToString() && userRole != global::User.Role.CompanyOwner.ToString())
            {
                return Forbid("Access denied. Only Admins and Managers can access this resource.");
            }

            var warehouses = await _context.Warehouses
                .Select(w => new WarehouseWithRspDto
                {
                    Id = w.Id,
                    Company = w.Company,
                    City = w.City,
                    DriverIdentificationNumber = _context.Users
                        .Where(u => u.WarehouseId == w.Id && u.UserRole == global::User.Role.Rsp)
                        .Select(u => u.IdentificationNumber)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(warehouses);
        }


        // GET: api/Warehouses/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Warehouse>> GetWarehouse(int id)
        {
            var warehouse = await _context.Warehouses.FindAsync(id);

            if (warehouse == null)
            {
                return NotFound();
            }

            return warehouse;
        }

        // PUT: api/Warehouses/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> PutWarehouse(int id, WarehouseUpsertDto dto)
        {
            if (id != dto.Id) return BadRequest();

            var wh = await _context.Warehouses.FirstOrDefaultAsync(w => w.Id == id);
            if (wh is null) return NotFound();

            // Detecta transición false -> true usando el valor REAL en BD
            bool wasHiringUpdated = !wh.IsHiring && dto.IsHiring;

            // Mapea SOLO lo que permites actualizar
            wh.Company = dto.Company;
            wh.City = dto.City;
            wh.Address = dto.Address;
            wh.IsHiring = dto.IsHiring;
            wh.UpdatedAt = DateTime.UtcNow;
            wh.OpenTime = ToTimeOnly(dto.OpenTime);
            wh.ZipCode = dto.ZipCode;
            wh.MetroId = dto.MetroId;
            wh.DriveRate = dto.DriveRate;

            // 1. Ids que deben quedar autorizados (lo que manda el front)
            var dtoIds = (dto.AuthorizedPersons ?? new List<AuthorizedPersonDto>())
                .Select(p => p.Id)
                .Distinct()
                .ToList();

            // 2. Permisos actuales en BD para este warehouse (UserPermit = 0)
            var existingPermits = await _context.Permits
                .Where(p => p.WarehouseId == wh.Id && p.UserPermit == 0)
                .ToListAsync();

            // 3. Eliminar los que ya no están en el DTO
            var toDelete = existingPermits
                .Where(p => !dtoIds.Contains(p.UserId))
                .ToList();

            if (toDelete.Any())
            {
                _context.Permits.RemoveRange(toDelete);
            }

            // 4. Agregar los que faltan (están en el DTO pero no en BD)
            var existingUserIds = existingPermits
                .Select(p => p.UserId)
                .ToHashSet();

            var toAdd = dtoIds
                .Where(id => !existingUserIds.Contains(id))
                .Select(id => new Permits
                {
                    UserId = id,
                    WarehouseId = wh.Id,
                    UserPermit = 0
                })
                .ToList();

            if (toAdd.Any())
            {
                await _context.Permits.AddRangeAsync(toAdd);
            }

            // 5. Guardar cambios
            await _context.SaveChangesAsync();


            if (wasHiringUpdated)
            {
                var applicants = await _context.Users
                    .Where(u => u.UserRole == global::User.Role.Applicant &&
                                !u.WasContacted &&
                                u.WarehouseId == wh.Id)
                    .Select(u => u.Id)
                    .ToListAsync();

                foreach (var applicantId in applicants)
                    await _applicantContactService.ContactApplicantAsync(applicantId);
            }

            return NoContent();
        }

        // POST: api/Warehouses
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [Authorize]
        [HttpPost]
        public async Task<ActionResult<WarehouseDTO>> PostWarehouse([FromBody] WarehouseDTO warehouseDTO)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            // 🔒 Solo permitir CompanyOwner y Admin
            if (userRole != "CompanyOwner" && userRole != "Admin")
            {
                return Forbid(); // 403 Forbidden
            }

            if (warehouseDTO == null)
                return BadRequest(new { Message = "Invalid warehouse data." });

            try
            {
                int? companyId = null;

                if (userRole == "CompanyOwner")
                {
                    // 🔍 Buscar la compañía donde el usuario es el dueño
                    companyId = await _context.Companies
                        .Where(c => c.OwnerId == userId)
                        .Select(c => c.Id)
                        .FirstOrDefaultAsync();
                }
                else if (userRole == "Admin")
                {
                    // 🔍 Tomar el CompanyId desde el usuario
                    companyId = await _context.Users
                        .Where(u => u.Id == userId)
                        .Select(u => u.CompanyId)
                        .FirstOrDefaultAsync();
                }

                if (!companyId.HasValue)
                    return BadRequest(new { Message = "No associated company found for this user." });

                // 🧷 Forzar el CompanyId
                warehouseDTO.CompanyId = companyId.Value;

                // Mapear y guardar
                var warehouse = _mapper.Map<Warehouse>(warehouseDTO);
                _context.Warehouses.Add(warehouse);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetWarehouse), new { id = warehouse.Id }, warehouseDTO);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Error saving warehouse", Error = ex.Message });
            }
        }



        // DELETE: api/Warehouses/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWarehouse(int id)
        {
            var warehouse = await _context.Warehouses.FindAsync(id);
            if (warehouse == null)
            {
                return NotFound();
            }

            _context.Warehouses.Remove(warehouse);
            await _context.SaveChangesAsync();


            return NoContent();
        }

        private bool WarehouseExists(int id)
        {
            return _context.Warehouses.Any(e => e.Id == id);
        }
        [HttpGet("metros/{companyId}")]
        public async Task<IActionResult> GetMetrosByCompany(int companyId)
        {
            var metros = _context.Metro.Where(m => m.CompanyId == companyId).ToList();

            return Ok(metros);
        }
    }
    public class WarehouseWithRspDto
    {
        public int Id { get; set; }
        public string? Company { get; set; }
        public string? City { get; set; }
        public string? DriverIdentificationNumber { get; set; }
    }

    public sealed class PublicWarehouseDto
    {
        public int Id { get; set; }
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Address { get; set; } = "";
    }
}
