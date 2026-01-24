using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PayrollPenaltyRulesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public PayrollPenaltyRulesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: api/PayrollPenaltyRules?configId=1&type=Damaged&activeOnly=true
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PayrollPenaltyRuleDto>>> GetAll(
        [FromQuery] int? configId,
        [FromQuery] PenaltyType? type,
        [FromQuery] bool activeOnly = false
    )
    {
        var q = _context.PayrollPenaltyRules.AsNoTracking().AsQueryable();

        if (configId.HasValue) q = q.Where(x => x.PayrollConfigId == configId.Value);
        if (type.HasValue) q = q.Where(x => x.Type == type.Value);
        if (activeOnly) q = q.Where(x => x.IsActive);

        var data = await q
            .OrderBy(x => x.PayrollConfigId)
            .ThenBy(x => x.Type)
            .Select(x => new PayrollPenaltyRuleDto
            {
                Id = x.Id,
                PayrollConfigId = x.PayrollConfigId,
                Type = x.Type,
                Amount = x.Amount,
                ApplyPerOccurrence = x.ApplyPerOccurrence,
                MaxOccurrencesPerWeek = x.MaxOccurrencesPerWeek,
                IsActive = x.IsActive
            })
            .ToListAsync();

        return Ok(data);
    }

    // GET: api/PayrollPenaltyRules/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PayrollPenaltyRuleDto>> GetById(int id)
    {
        var x = await _context.PayrollPenaltyRules.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new PayrollPenaltyRuleDto
            {
                Id = r.Id,
                PayrollConfigId = r.PayrollConfigId,
                Type = r.Type,
                Amount = r.Amount,
                ApplyPerOccurrence = r.ApplyPerOccurrence,
                MaxOccurrencesPerWeek = r.MaxOccurrencesPerWeek,
                IsActive = r.IsActive
            })
            .FirstOrDefaultAsync();

        if (x is null) return NotFound($"PayrollPenaltyRule {id} no existe.");
        return Ok(x);
    }

    // POST: api/PayrollPenaltyRules
    [HttpPost]
    public async Task<ActionResult<PayrollPenaltyRuleDto>> Create([FromBody] PayrollPenaltyRuleCreateDto dto)
    {
        var configExists = await _context.PayrollConfigs.AnyAsync(c => c.Id == dto.PayrollConfigId);
        if (!configExists) return BadRequest($"PayrollConfigId {dto.PayrollConfigId} no existe.");

        // evitar duplicado (config + type)
        var duplicate = await _context.PayrollPenaltyRules.AnyAsync(x =>
            x.PayrollConfigId == dto.PayrollConfigId &&
            x.Type == dto.Type);

        if (duplicate)
            return Conflict("Ya existe una regla de penalty para ese ConfigId y Type.");

        if (dto.Amount < 0) return BadRequest("Amount debe ser positivo (el sistema aplica el signo al calcular).");
        if (dto.MaxOccurrencesPerWeek.HasValue && dto.MaxOccurrencesPerWeek.Value < 1)
            return BadRequest("MaxOccurrencesPerWeek debe ser >= 1 o null.");

        var entity = new PayrollPenaltyRule
        {
            PayrollConfigId = dto.PayrollConfigId,
            Type = dto.Type,
            Amount = dto.Amount,
            ApplyPerOccurrence = dto.ApplyPerOccurrence,
            MaxOccurrencesPerWeek = dto.MaxOccurrencesPerWeek,
            IsActive = dto.IsActive
        };

        _context.PayrollPenaltyRules.Add(entity);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, new PayrollPenaltyRuleDto
        {
            Id = entity.Id,
            PayrollConfigId = entity.PayrollConfigId,
            Type = entity.Type,
            Amount = entity.Amount,
            ApplyPerOccurrence = entity.ApplyPerOccurrence,
            MaxOccurrencesPerWeek = entity.MaxOccurrencesPerWeek,
            IsActive = entity.IsActive
        });
    }

    // PUT: api/PayrollPenaltyRules/5
    [HttpPut("{id:int}")]
    public async Task<ActionResult<PayrollPenaltyRuleDto>> Update(int id, [FromBody] PayrollPenaltyRuleUpdateDto dto)
    {
        var entity = await _context.PayrollPenaltyRules.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound($"PayrollPenaltyRule {id} no existe.");

        if (dto.Type.HasValue) entity.Type = dto.Type.Value;
        if (dto.Amount.HasValue)
        {
            if (dto.Amount.Value < 0) return BadRequest("Amount debe ser positivo.");
            entity.Amount = dto.Amount.Value;
        }
        if (dto.ApplyPerOccurrence.HasValue) entity.ApplyPerOccurrence = dto.ApplyPerOccurrence.Value;

        // permite setear a null para “sin límite”
        if (dto.MaxOccurrencesPerWeek.HasValue && dto.MaxOccurrencesPerWeek.Value < 1)
            return BadRequest("MaxOccurrencesPerWeek debe ser >= 1 o null.");

        if (dto.MaxOccurrencesPerWeek is not null) entity.MaxOccurrencesPerWeek = dto.MaxOccurrencesPerWeek;
        if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;

        // validar duplicado después de cambiar Type
        var duplicate = await _context.PayrollPenaltyRules.AnyAsync(x =>
            x.Id != id &&
            x.PayrollConfigId == entity.PayrollConfigId &&
            x.Type == entity.Type);

        if (duplicate)
            return Conflict("Con esos cambios, quedaría duplicada (ConfigId + Type).");

        await _context.SaveChangesAsync();

        return Ok(new PayrollPenaltyRuleDto
        {
            Id = entity.Id,
            PayrollConfigId = entity.PayrollConfigId,
            Type = entity.Type,
            Amount = entity.Amount,
            ApplyPerOccurrence = entity.ApplyPerOccurrence,
            MaxOccurrencesPerWeek = entity.MaxOccurrencesPerWeek,
            IsActive = entity.IsActive
        });
    }

    // PATCH: api/PayrollPenaltyRules/5/toggle?active=false
    [HttpPatch("{id:int}/toggle")]
    public async Task<IActionResult> Toggle(int id, [FromQuery] bool active)
    {
        var entity = await _context.PayrollPenaltyRules.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound();

        entity.IsActive = active;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/PayrollPenaltyRules/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _context.PayrollPenaltyRules.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound();

        _context.PayrollPenaltyRules.Remove(entity);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
