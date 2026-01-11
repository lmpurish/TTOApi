using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Scaffolding.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ZonesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public string Message { get; private set; }

        public ZonesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/Zones
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Zone>>> GetZones()
        {
            return await _context.Zones.ToListAsync();
        }


        [HttpGet("GetZonesByManager")]
        public async Task<ActionResult<IEnumerable<Zone>>> GetZonesByManager([FromQuery] int? warehouseId)
        {
            if (!warehouseId.HasValue)
                return BadRequest(new { Message = "El parámetro warehouseId es requerido." });

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
                return Unauthorized("Usuario no autenticado");

            int userID = int.Parse(userIdClaim.Value);
            var user = await _context.Users.FindAsync(userID);

            if (user == null)
                return Unauthorized("Usuario no encontrado");

            // Validar si el usuario es Manager y está intentando acceder a otro almacén
            if (user.UserRole == global::User.Role.Manager)
            {
                if (user.WarehouseId != warehouseId.Value)
                {
                    return Unauthorized(new { Message = "No tiene permiso para ver zonas de este almacén." });
                }
            }

            var zones = await _context.Zones
                .Where(z => z.IdWarehouse == warehouseId.Value)
                .ToListAsync();

            return Ok(zones);
        }


        
        [HttpGet("GetZonesWarehouse/{warehouseId}")]
        public async Task<ActionResult<IEnumerable<Zone>>> GetZonesWarehouse(int warehouseId)
        {
            var zones = await _context.Zones
                .Where(w => w.IdWarehouse == warehouseId)
                .ToListAsync();

            return Ok(zones);
        }




        // GET: api/Zones/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Zone>> GetZone(int id)
        {
            var zone = await _context.Zones.FindAsync(id);

            if (zone == null)
            {
                return NotFound();
            }

            return zone;
        }

        // PUT: api/Zones/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutZone(int id, [FromBody] Zone incoming)
        {
            if (id != incoming.Id) return BadRequest();

            var existing = await _context.Zones.FirstOrDefaultAsync(z => z.Id == id);
            if (existing == null) return NotFound();

            // Actualiza solo lo que quieres permitir
            existing.ZoneCode = incoming.ZoneCode?.Trim();
            existing.PriceStop = incoming.PriceStop;
            existing.IdWarehouse = incoming.IdWarehouse;
            existing.Area = incoming.Area?.Trim();
            existing.ZipCodesSerialized = incoming.ZipCodesSerialized;
           

            await _context.SaveChangesAsync();
            return NoContent();
        }


        // POST: api/Zones
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Zone>> PostZone(Zone zone)
        {
            // Limpia strings y normaliza ZIPs antes de guardar
            zone.ZoneCode = zone.ZoneCode?.Trim();
            zone.Area = zone.Area?.Trim();
            zone.ZipCodesSerialized = NormalizeZipCsv(zone.ZipCodesSerialized);

            _context.Zones.Add(zone);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetZone), new { id = zone.Id }, zone);
        }

        private static string? NormalizeZipCsv(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return null;

            var parts = csv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => !string.IsNullOrWhiteSpace(p));

            var normalized = string.Join(",", parts);
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }


        // DELETE: api/Zones/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteZone(int id)
        {
            var zone = await _context.Zones.FindAsync(id);
            if (zone == null)
            {
                return NotFound();
            }

            _context.Zones.Remove(zone);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ZoneExists(int id)
        {
            return _context.Zones.Any(e => e.Id == id);
        }
    }
}
