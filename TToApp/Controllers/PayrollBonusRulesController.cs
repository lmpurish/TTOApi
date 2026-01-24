using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PayrollBonusRulesController : ControllerBase
{
    private readonly ApplicationDbContext _context; // ajusta tu DbContext

    public PayrollBonusRulesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: api/PayrollBonusRules?configId=1&type=HighOnTime&activeOnly=true
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PayrollBonusRuleDto>>> GetAll(
        [FromQuery] int? configId,
        [FromQuery] BonusType? type,
        [FromQuery] bool activeOnly = false
    )
    {
        var q = _context.PayrollBonusRules.AsNoTracking().AsQueryable();

        if (configId.HasValue) q = q.Where(x => x.PayrollConfigId == configId.Value);
        if (type.HasValue) q = q.Where(x => x.Type == type.Value);
        if (activeOnly) q = q.Where(x => x.IsActive);

        var data = await q
            .OrderBy(x => x.PayrollConfigId)
            .ThenBy(x => x.Type)
            .ThenBy(x => x.Threshold)
            .Select(x => new PayrollBonusRuleDto
            {
                Id = x.Id,
                PayrollConfigId = x.PayrollConfigId,
                Type = x.Type,
                Threshold = x.Threshold,
                Amount = x.Amount,
                IsActive = x.IsActive
            })
            .ToListAsync();

        return Ok(data);
    }

    // GET: api/PayrollBonusRules/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PayrollBonusRuleDto>> GetById(int id)
    {
        var x = await _context.PayrollBonusRules.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new PayrollBonusRuleDto
            {
                Id = r.Id,
                PayrollConfigId = r.PayrollConfigId,
                Type = r.Type,
                Threshold = r.Threshold,
                Amount = r.Amount,
                IsActive = r.IsActive
            })
            .FirstOrDefaultAsync();

        if (x is null) return NotFound($"PayrollBonusRule {id} no existe.");
        return Ok(x);
    }

    // POST: api/PayrollBonusRules
    [HttpPost]
    public async Task<ActionResult<PayrollBonusRuleDto>> Create([FromBody] PayrollBonusRuleCreateDto dto)
    {
        var configExists = await _context.PayrollConfigs.AnyAsync(c => c.Id == dto.PayrollConfigId);
        if (!configExists) return BadRequest($"PayrollConfigId {dto.PayrollConfigId} no existe.");

        // Opcional: evitar duplicado por (ConfigId + Type + Threshold)
        var duplicate = await _context.PayrollBonusRules.AnyAsync(x =>
            x.PayrollConfigId == dto.PayrollConfigId &&
            x.Type == dto.Type &&
            x.Threshold == dto.Threshold);

        if (duplicate)
            return Conflict("Ya existe una regla con el mismo ConfigId + Type + Threshold.");

        var entity = new PayrollBonusRule
        {
            PayrollConfigId = dto.PayrollConfigId,
            Type = dto.Type,
            Threshold = dto.Threshold,
            Amount = dto.Amount,
            IsActive = dto.IsActive
        };

        _context.PayrollBonusRules.Add(entity);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, new PayrollBonusRuleDto
        {
            Id = entity.Id,
            PayrollConfigId = entity.PayrollConfigId,
            Type = entity.Type,
            Threshold = entity.Threshold,
            Amount = entity.Amount,
            IsActive = entity.IsActive
        });
    }

    // PUT: api/PayrollBonusRules/5
    [HttpPut("{id:int}")]
    public async Task<ActionResult<PayrollBonusRuleDto>> Update(int id, [FromBody] PayrollBonusRuleUpdateDto dto)
    {
        var entity = await _context.PayrollBonusRules.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound($"PayrollBonusRule {id} no existe.");

        if (dto.Type.HasValue) entity.Type = dto.Type.Value;
        if (dto.Threshold is not null) entity.Threshold = dto.Threshold; // permite null
        if (dto.Amount.HasValue) entity.Amount = dto.Amount.Value;
        if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;

        // Opcional: validación duplicado después del cambio
        var duplicate = await _context.PayrollBonusRules.AnyAsync(x =>
            x.Id != id &&
            x.PayrollConfigId == entity.PayrollConfigId &&
            x.Type == entity.Type &&
            x.Threshold == entity.Threshold);

        if (duplicate)
            return Conflict("Con esos cambios, la regla quedaría duplicada (ConfigId + Type + Threshold).");

        await _context.SaveChangesAsync();

        return Ok(new PayrollBonusRuleDto
        {
            Id = entity.Id,
            PayrollConfigId = entity.PayrollConfigId,
            Type = entity.Type,
            Threshold = entity.Threshold,
            Amount = entity.Amount,
            IsActive = entity.IsActive
        });
    }

    // PATCH: api/PayrollBonusRules/5/toggle?active=false
    [HttpPatch("{id:int}/toggle")]
    public async Task<IActionResult> Toggle(int id, [FromQuery] bool active)
    {
        var entity = await _context.PayrollBonusRules.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound();

        entity.IsActive = active;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/PayrollBonusRules/5  (si prefieres borrar en vez de soft-disable)
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _context.PayrollBonusRules.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound();

        _context.PayrollBonusRules.Remove(entity);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
