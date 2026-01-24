using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PayrollConfigsController : ControllerBase
{
    private readonly ApplicationDbContext _context; // ajusta el nombre real

    public PayrollConfigsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: api/PayrollConfigs?warehouseId=3&includeRules=false
    // [HttpGet]
    // public async Task<ActionResult<IEnumerable<PayrollConfigDto>>> GetAll(
    //     [FromQuery] int? warehouseId,
    //     [FromQuery] bool includeRules = false
    // )
    // {
    //     IQueryable<PayrollConfig> q = _context.PayrollConfigs.AsNoTracking();

    //     if (warehouseId.HasValue)
    //         q = q.Where(x => x.WarehouseId == warehouseId.Value);

    //     if (includeRules)
    //     {
    //         q = q.Include(x => x.WeightRules)
    //              .Include(x => x.PenaltyRules)
    //              .Include(x => x.BonusRules);
    //     }

    //     var data = await q
    //         .OrderByDescending(x => x.IsActive)
    //         .ThenBy(x => x.WarehouseId)
    //         .Select(x => new PayrollConfigDto
    //         {
    //             Id = x.Id,
    //             WarehouseId = x.WarehouseId,
    //             EnableWeightExtra = x.EnableWeightExtra,
    //             EnablePenalties = x.EnablePenalties,
    //             EnableBonuses = x.EnableBonuses,
    //             DefaultPenaltyAmount = x.DefaultPenaltyAmount,
    //             PenaltyCapPerWeek = x.PenaltyCapPerWeek,
    //             IsActive = x.IsActive,
    //             CreatedAt = x.CreatedAt,
    //             UpdatedAt = x.UpdatedAt,

    //             WeightRulesCount = includeRules ? x.WeightRules.Count : 0,
    //             PenaltyRulesCount = includeRules ? x.PenaltyRules.Count : 0,
    //             BonusRulesCount = includeRules ? x.BonusRules.Count : 0
    //         })
    //         .ToListAsync();

    //     return Ok(data);
    // }

    // GET: api/PayrollConfigs/5?includeRules=true
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PayrollConfig>> GetById(int id, [FromQuery] bool includeRules = false)
    {
        IQueryable<PayrollConfig> q = _context.PayrollConfigs.AsNoTracking().Where(x => x.Id == id);

        if (includeRules)
        {
            q = q.Include(x => x.WeightRules)
                 .Include(x => x.PenaltyRules)
                 .Include(x => x.BonusRules);
        }

        var entity = await q.FirstOrDefaultAsync();
        if (entity is null) return NotFound($"PayrollConfig {id} no existe.");

        // OJO: aquí devolvemos entidad (incluye reglas si pediste). Si prefieres DTO, dímelo y lo mapeo.
        return Ok(entity);
    }

    // GET: api/PayrollConfigs/by-warehouse/3?includeRules=true
    [HttpGet("by-warehouse/{warehouseId:int}")]
    public async Task<ActionResult<PayrollConfig>> GetByWarehouseId(int warehouseId, [FromQuery] bool includeRules = false)
    {
        IQueryable<PayrollConfig> q = _context.PayrollConfigs.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId);

        if (includeRules)
        {
            q = q.Include(x => x.WeightRules)
                 .Include(x => x.PenaltyRules)
                 .Include(x => x.BonusRules);
        }

        var entity = await q.FirstOrDefaultAsync();
        if (entity is null) return NotFound($"No existe PayrollConfig para WarehouseId {warehouseId}.");

        return Ok(entity);
    }

    // POST: api/PayrollConfigs
    // Crea o devuelve conflicto si ya existe config para ese WarehouseId
    [HttpPost]
    public async Task<ActionResult<PayrollConfigDto>> Create([FromBody] PayrollConfigUpsertDto dto)
    {
        var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId);
        if (!warehouseExists) return BadRequest($"WarehouseId {dto.WarehouseId} no existe.");

        var exists = await _context.PayrollConfigs.AnyAsync(x => x.WarehouseId == dto.WarehouseId);
        if (exists) return Conflict($"Ya existe una configuración para WarehouseId {dto.WarehouseId}.");

        var entity = new PayrollConfig
        {
            WarehouseId = dto.WarehouseId,
            EnableWeightExtra = dto.EnableWeightExtra,
            EnablePenalties = dto.EnablePenalties,
            EnableBonuses = dto.EnableBonuses,
            DefaultPenaltyAmount = dto.DefaultPenaltyAmount,
            PenaltyCapPerWeek = dto.PenaltyCapPerWeek,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = null
        };

        _context.PayrollConfigs.Add(entity);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entity.Id, includeRules = false }, new PayrollConfigDto
        {
            Id = entity.Id,
            WarehouseId = entity.WarehouseId,
            EnableWeightExtra = entity.EnableWeightExtra,
            EnablePenalties = entity.EnablePenalties,
            EnableBonuses = entity.EnableBonuses,
            DefaultPenaltyAmount = entity.DefaultPenaltyAmount,
            PenaltyCapPerWeek = entity.PenaltyCapPerWeek,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            WeightRulesCount = 0,
            PenaltyRulesCount = 0,
            BonusRulesCount = 0
        });
    }

    // PUT: api/PayrollConfigs/5
    [HttpPut("{id:int}")]
    public async Task<ActionResult<PayrollConfigDto>> Update(int id, [FromBody] PayrollConfigUpsertDto dto)
    {
        var entity = await _context.PayrollConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound($"PayrollConfig {id} no existe.");

        // si WarehouseId cambia, valida y evita duplicados
        if (entity.WarehouseId != dto.WarehouseId)
        {
            var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId);
            if (!warehouseExists) return BadRequest($"WarehouseId {dto.WarehouseId} no existe.");

            var duplicate = await _context.PayrollConfigs.AnyAsync(x => x.WarehouseId == dto.WarehouseId && x.Id != id);
            if (duplicate) return Conflict($"Ya existe config para WarehouseId {dto.WarehouseId}.");
        }

        entity.WarehouseId = dto.WarehouseId;
        entity.EnableWeightExtra = dto.EnableWeightExtra;
        entity.EnablePenalties = dto.EnablePenalties;
        entity.EnableBonuses = dto.EnableBonuses;
        entity.DefaultPenaltyAmount = dto.DefaultPenaltyAmount;
        entity.PenaltyCapPerWeek = dto.PenaltyCapPerWeek;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // counts rápidos (sin Includes)
        var counts = await _context.PayrollConfigs
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                Weight = x.WeightRules.Count,
                Penalty = x.PenaltyRules.Count,
                Bonus = x.BonusRules.Count
            })
            .FirstAsync();

        return Ok(new PayrollConfigDto
        {
            Id = entity.Id,
            WarehouseId = entity.WarehouseId,
            EnableWeightExtra = entity.EnableWeightExtra,
            EnablePenalties = entity.EnablePenalties,
            EnableBonuses = entity.EnableBonuses,
            DefaultPenaltyAmount = entity.DefaultPenaltyAmount,
            PenaltyCapPerWeek = entity.PenaltyCapPerWeek,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            WeightRulesCount = counts.Weight,
            PenaltyRulesCount = counts.Penalty,
            BonusRulesCount = counts.Bonus
        });
    }

    // POST: api/PayrollConfigs/upsert
    // crea si no existe, actualiza si existe por WarehouseId
    [HttpPost("upsert")]
    public async Task<ActionResult<PayrollConfigDto>> Upsert([FromBody] PayrollConfigUpsertDto dto)
    {
        var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId);
        if (!warehouseExists) return BadRequest($"WarehouseId {dto.WarehouseId} no existe.");

        var entity = await _context.PayrollConfigs.FirstOrDefaultAsync(x => x.WarehouseId == dto.WarehouseId);

        if (entity is null)
        {
            entity = new PayrollConfig
            {
                WarehouseId = dto.WarehouseId,
                EnableWeightExtra = dto.EnableWeightExtra,
                EnablePenalties = dto.EnablePenalties,
                EnableBonuses = dto.EnableBonuses,
                DefaultPenaltyAmount = dto.DefaultPenaltyAmount,
                PenaltyCapPerWeek = dto.PenaltyCapPerWeek,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            _context.PayrollConfigs.Add(entity);
        }
        else
        {
            entity.EnableWeightExtra = dto.EnableWeightExtra;
            entity.EnablePenalties = dto.EnablePenalties;
            entity.EnableBonuses = dto.EnableBonuses;
            entity.DefaultPenaltyAmount = dto.DefaultPenaltyAmount;
            entity.PenaltyCapPerWeek = dto.PenaltyCapPerWeek;
            entity.IsActive = dto.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return Ok(new PayrollConfigDto
        {
            Id = entity.Id,
            WarehouseId = entity.WarehouseId,
            EnableWeightExtra = entity.EnableWeightExtra,
            EnablePenalties = entity.EnablePenalties,
            EnableBonuses = entity.EnableBonuses,
            DefaultPenaltyAmount = entity.DefaultPenaltyAmount,
            PenaltyCapPerWeek = entity.PenaltyCapPerWeek,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        });
    }
}
