using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/zone-weight-rules")]
    [ApiController]
    [Authorize]
    public class ZoneWeightRuleController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ZoneWeightRuleController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ZoneWeightRule>>> GetByZone(
            [FromQuery] int zoneId,
            [FromQuery] bool? activeOnly = true,
            CancellationToken ct = default)
        {
            var query = _db.ZoneWeightRules.Where(r => r.ZoneId == zoneId);
            if (activeOnly == true)
                query = query.Where(r => r.IsActive);

            var rules = await query
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.MinWeight)
                .ToListAsync(ct);

            return Ok(rules);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ZoneWeightRule>> GetById(int id, CancellationToken ct)
        {
            var rule = await _db.ZoneWeightRules.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rule is null) return NotFound();
            return Ok(rule);
        }

        [HttpPost]
        public async Task<ActionResult<ZoneWeightRule>> Create(
            [FromBody] ZoneWeightRuleRequest body,
            CancellationToken ct)
        {
            var zoneExists = await _db.Set<Zone>().AnyAsync(z => z.Id == body.ZoneId, ct);
            if (!zoneExists)
                return BadRequest(new { message = $"Zone {body.ZoneId} not found." });

            if (body.MaxWeight.HasValue && body.MaxWeight <= body.MinWeight)
                return BadRequest(new { message = "MaxWeight must be greater than MinWeight." });

            var rule = new ZoneWeightRule
            {
                ZoneId        = body.ZoneId,
                MinWeight     = body.MinWeight,
                MaxWeight     = body.MaxWeight,
                ExtraAmount   = body.ExtraAmount,
                Priority      = body.Priority,
                IsActive      = body.IsActive,
                Version       = 1,
                EffectiveFrom = body.EffectiveFrom ?? DateTime.UtcNow,
                EffectiveTo   = body.EffectiveTo,
                CreatedAt     = DateTime.UtcNow,
                CreatedBy     = body.CreatedBy
            };

            _db.ZoneWeightRules.Add(rule);
            await _db.SaveChangesAsync(ct);
            return CreatedAtAction(nameof(GetById), new { id = rule.Id }, rule);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<ZoneWeightRule>> Update(
            int id,
            [FromBody] ZoneWeightRuleRequest body,
            CancellationToken ct)
        {
            var rule = await _db.ZoneWeightRules.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rule is null) return NotFound();

            if (body.MaxWeight.HasValue && body.MaxWeight <= body.MinWeight)
                return BadRequest(new { message = "MaxWeight must be greater than MinWeight." });

            rule.MinWeight   = body.MinWeight;
            rule.MaxWeight   = body.MaxWeight;
            rule.ExtraAmount = body.ExtraAmount;
            rule.Priority    = body.Priority;
            rule.IsActive    = body.IsActive;
            rule.EffectiveFrom = body.EffectiveFrom ?? rule.EffectiveFrom;
            rule.EffectiveTo   = body.EffectiveTo;

            await _db.SaveChangesAsync(ct);
            return Ok(rule);
        }

        [HttpPost("{id:int}/new-version")]
        public async Task<ActionResult<ZoneWeightRule>> NewVersion(
            int id,
            [FromBody] ZoneWeightRuleVersionRequest body,
            CancellationToken ct)
        {
            var current = await _db.ZoneWeightRules.FirstOrDefaultAsync(r => r.Id == id && r.IsActive, ct);
            if (current is null) return NotFound(new { message = $"Active rule {id} not found." });

            if (body.MaxWeight.HasValue && body.MaxWeight <= body.MinWeight)
                return BadRequest(new { message = "MaxWeight must be greater than MinWeight." });

            var now = DateTime.UtcNow;

            current.IsActive    = false;
            current.EffectiveTo = now;

            var newRule = new ZoneWeightRule
            {
                ZoneId        = current.ZoneId,
                MinWeight     = body.MinWeight,
                MaxWeight     = body.MaxWeight,
                ExtraAmount   = body.ExtraAmount,
                Priority      = body.Priority ?? current.Priority,
                IsActive      = true,
                Version       = current.Version + 1,
                EffectiveFrom = now,
                EffectiveTo   = body.EffectiveTo,
                CreatedAt     = now,
                CreatedBy     = body.CreatedBy
            };

            _db.ZoneWeightRules.Add(newRule);
            await _db.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetById), new { id = newRule.Id }, new
            {
                previous = new { current.Id, current.Version, deactivatedAt = current.EffectiveTo },
                current  = newRule
            });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var rule = await _db.ZoneWeightRules.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rule is null) return NotFound();

            rule.IsActive = false;
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }
    }

    public sealed class ZoneWeightRuleRequest
    {
        public int ZoneId { get; set; }
        public decimal MinWeight { get; set; }
        public decimal? MaxWeight { get; set; }
        public decimal ExtraAmount { get; set; }
        public int Priority { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public int? CreatedBy { get; set; }
    }

    public sealed class ZoneWeightRuleVersionRequest
    {
        public decimal MinWeight { get; set; }
        public decimal? MaxWeight { get; set; }
        public decimal ExtraAmount { get; set; }
        public int? Priority { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public int? CreatedBy { get; set; }
    }
}
