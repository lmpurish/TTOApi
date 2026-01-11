using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WarehouseMessageTemplatesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public WarehouseMessageTemplatesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/WarehouseMessageTemplates
        [HttpGet("allMessageByWarehouse/{warehouseId}")]
        public async Task<ActionResult<IEnumerable<WarehouseMessageTemplate>>> GetWarehouseMessageTemplates(int warehouseId)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
            if (userIdClaim == null)
                return Unauthorized(new { Message = "Invalid token" });

            var warehouseMessageTemplate = await _context.WarehouseMessageTemplates
                .Where(w => w.WarehouseId == warehouseId).ToListAsync();

            if (warehouseMessageTemplate == null || !warehouseMessageTemplate.Any())
            {
                return NotFound();
            }

            return Ok(warehouseMessageTemplate);
        }

        // GET: api/WarehouseMessageTemplates/5
        [HttpGet("{id}")]
        public async Task<ActionResult<WarehouseMessageTemplate>> GetWarehouseMessageTemplate(int id)
        {
            var warehouseMessageTemplate = await _context.WarehouseMessageTemplates.FindAsync(id);

            if (warehouseMessageTemplate == null)
            {
                return NotFound();
            }

            return warehouseMessageTemplate;
        }

        [Authorize]
        [HttpGet("warehouse/{warehouseId}")]
        public async Task<ActionResult<WarehouseMessageTemplate>> GetWMTbyWarehouse(int warehouseId)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
            if (userIdClaim == null)
                return Unauthorized(new { Message = "Invalid token" });

            var warehouseMessageTemplate = await _context.WarehouseMessageTemplates
                .FirstOrDefaultAsync(w => w.WarehouseId == warehouseId && w.IsDefault);

            if (warehouseMessageTemplate == null)
            {
                return NotFound();
            }

            return warehouseMessageTemplate;
        }

        // PUT: api/WarehouseMessageTemplates/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutWarehouseMessageTemplate(int id, WarehouseMessageTemplate warehouseMessageTemplate)
        {
            if (id != warehouseMessageTemplate.Id)
            {
                return BadRequest();
            }

            _context.Entry(warehouseMessageTemplate).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!WarehouseMessageTemplateExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/WarehouseMessageTemplates
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<WarehouseMessageTemplate>> PostWarehouseMessageTemplate(WarehouseMessageTemplate warehouseMessageTemplate)
        {
            _context.WarehouseMessageTemplates.Add(warehouseMessageTemplate);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetWarehouseMessageTemplate", new { id = warehouseMessageTemplate.Id }, warehouseMessageTemplate);
        }

        // DELETE: api/WarehouseMessageTemplates/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWarehouseMessageTemplate(int id)
        {
            var warehouseMessageTemplate = await _context.WarehouseMessageTemplates.FindAsync(id);
            if (warehouseMessageTemplate == null)
            {
                return NotFound();
            }

            _context.WarehouseMessageTemplates.Remove(warehouseMessageTemplate);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool WarehouseMessageTemplateExists(int id)
        {
            return _context.WarehouseMessageTemplates.Any(e => e.Id == id);
        }
    }
}
