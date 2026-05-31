using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/zone-pay-rules")]
    [ApiController]
    [Authorize]
    public class ZonePayRuleController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ZonePayRuleController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ZonePayRule>>> GetByZone(
            [FromQuery] int zoneId,
            [FromQuery] bool? activeOnly = true,
            CancellationToken ct = default)
        {
            var query = _db.ZonePayRules.Where(r => r.ZoneId == zoneId);
            if (activeOnly == true)
                query = query.Where(r => r.IsActive);

            var rules = await query
                .OrderBy(r => r.PaymentType)
                .ThenBy(r => r.MinPackages)
                .ThenByDescending(r => r.EffectiveFrom)
                .ToListAsync(ct);

            return Ok(rules);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ZonePayRule>> GetById(int id, CancellationToken ct)
        {
            var rule = await _db.ZonePayRules.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rule is null) return NotFound();
            return Ok(rule);
        }

        [HttpPost]
        public async Task<ActionResult<ZonePayRule>> Create(
            [FromBody] ZonePayRuleRequest body,
            CancellationToken ct)
        {
            var zoneExists = await _db.Set<Zone>().AnyAsync(z => z.Id == body.ZoneId, ct);
            if (!zoneExists)
                return BadRequest(new { message = $"Zone {body.ZoneId} not found." });

            if (body.MinPackages.HasValue && body.MaxPackages.HasValue && body.MinPackages > body.MaxPackages)
                return BadRequest(new { message = "MinPackages cannot be greater than MaxPackages." });

            var rule = new ZonePayRule
            {
                ZoneId             = body.ZoneId,
                PaymentType        = body.PaymentType,
                BaseAmount         = body.BaseAmount,
                ExtraAmount        = body.ExtraAmount,
                MinPackages        = body.MinPackages,
                MaxPackages        = body.MaxPackages,
                UseDriverRateForExtra = body.UseDriverRateForExtra,
                Version            = body.Version,
                IsActive           = body.IsActive,
                EffectiveFrom      = body.EffectiveFrom ?? DateTime.UtcNow,
                EffectiveTo        = body.EffectiveTo,
                CreatedAt          = DateTime.UtcNow,
                CreatedBy          = body.CreatedBy
            };

            _db.ZonePayRules.Add(rule);
            await _db.SaveChangesAsync(ct);
            return CreatedAtAction(nameof(GetById), new { id = rule.Id }, rule);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<ZonePayRule>> Update(
            int id,
            [FromBody] ZonePayRuleRequest body,
            CancellationToken ct)
        {
            var rule = await _db.ZonePayRules.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rule is null) return NotFound();

            if (body.MinPackages.HasValue && body.MaxPackages.HasValue && body.MinPackages > body.MaxPackages)
                return BadRequest(new { message = "MinPackages cannot be greater than MaxPackages." });

            rule.PaymentType          = body.PaymentType;
            rule.BaseAmount           = body.BaseAmount;
            rule.ExtraAmount          = body.ExtraAmount;
            rule.MinPackages          = body.MinPackages;
            rule.MaxPackages          = body.MaxPackages;
            rule.UseDriverRateForExtra = body.UseDriverRateForExtra;
            rule.Version              = body.Version;
            rule.IsActive             = body.IsActive;
            rule.EffectiveFrom        = body.EffectiveFrom ?? DateTime.UtcNow;
            rule.EffectiveTo          = body.EffectiveTo;

            await _db.SaveChangesAsync(ct);
            return Ok(rule);
        }

        [HttpPost("{id:int}/new-version")]
        public async Task<ActionResult<ZonePayRule>> NewVersion(
            int id,
            [FromBody] ZonePayRuleVersionRequest body,
            CancellationToken ct)
        {
            var current = await _db.ZonePayRules.FirstOrDefaultAsync(r => r.Id == id && r.IsActive, ct);
            if (current is null) return NotFound(new { message = $"Active rule {id} not found." });

            if (body.MinPackages.HasValue && body.MaxPackages.HasValue && body.MinPackages > body.MaxPackages)
                return BadRequest(new { message = "MinPackages cannot be greater than MaxPackages." });

            var now = DateTime.UtcNow;

            // Desactivar la versión actual
            current.IsActive   = false;
            current.EffectiveTo = now;

            // Crear nueva versión
            var newRule = new ZonePayRule
            {
                ZoneId                = current.ZoneId,
                PaymentType           = body.PaymentType,
                BaseAmount            = body.BaseAmount,
                ExtraAmount           = body.ExtraAmount,
                MinPackages           = body.MinPackages,
                MaxPackages           = body.MaxPackages,
                UseDriverRateForExtra = body.UseDriverRateForExtra,
                Version               = current.Version + 1,
                IsActive              = true,
                EffectiveFrom         = now,
                EffectiveTo           = body.EffectiveTo,
                CreatedAt             = now,
                CreatedBy             = body.CreatedBy
            };

            _db.ZonePayRules.Add(newRule);
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
            var rule = await _db.ZonePayRules.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rule is null) return NotFound();

            rule.IsActive = false;
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }
    }

    public sealed class ZonePayRuleVersionRequest
    {
        public PaymentType PaymentType { get; set; }
        public decimal? BaseAmount { get; set; }
        public decimal? ExtraAmount { get; set; }
        public int? MinPackages { get; set; }
        public int? MaxPackages { get; set; }
        public bool UseDriverRateForExtra { get; set; } = true;
        public DateTime? EffectiveTo { get; set; }
        public int? CreatedBy { get; set; }
    }

    public sealed class ZonePayRuleRequest
    {
        public int ZoneId { get; set; }
        public PaymentType PaymentType { get; set; }
        public decimal? BaseAmount { get; set; }
        public decimal? ExtraAmount { get; set; }
        public int? MinPackages { get; set; }
        public int? MaxPackages { get; set; }
        public bool UseDriverRateForExtra { get; set; } = true;
        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public int? CreatedBy { get; set; }
    }
}
