using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.Model;
using TToApp.Services.EarlyWarnings;
using TToApp.Constants;

namespace TToApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EarlyWarningsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public EarlyWarningsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] int? companyId,
            [FromQuery] int? warehouseId,
            [FromQuery] string? type,
            [FromQuery] string? status = "Open")
        {
            var query = _db.EarlyWarnings.AsNoTracking().AsQueryable();

            if (companyId.HasValue)
                query = query.Where(x => x.CompanyId == companyId.Value);

            if (warehouseId.HasValue)
                query = query.Where(x => x.WarehouseId == warehouseId.Value);

            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(x => x.Type == type);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status);

            var data = await query
                .OrderByDescending(x => x.CreatedAt)
                .Take(200)
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var warning = await _db.EarlyWarnings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (warning == null)
                return NotFound("EarlyWarning no existe.");

            return Ok(warning);
        }

        [HttpPost("{id:long}/review")]
        public async Task<IActionResult> MarkAsReviewed(long id)
        {
            var warning = await _db.EarlyWarnings.FindAsync(id);

            if (warning == null)
                return NotFound("EarlyWarning no existe.");

            warning.Status = "Reviewed";
            warning.ReviewedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(warning);
        }

        [HttpPost("{id:long}/close")]
        public async Task<IActionResult> Close(long id)
        {
            var warning = await _db.EarlyWarnings.FindAsync(id);

            if (warning == null)
                return NotFound("EarlyWarning no existe.");

            warning.Status = "Closed";
            warning.ReviewedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(warning);
        }

        [HttpPost("{id:long}/reopen")]
        public async Task<IActionResult> Reopen(long id)
        {
            var warning = await _db.EarlyWarnings.FindAsync(id);

            if (warning == null)
                return NotFound("EarlyWarning no existe.");

            warning.Status = "Open";
            warning.ReviewedAt = null;
            warning.ReviewedBy = null;

            await _db.SaveChangesAsync();

            return Ok(warning);
        }

        [HttpPost("run-hiring-capacity")]
        public async Task<IActionResult> RunHiringCapacity(
            [FromServices] IEarlyWarningService earlyWarningService,
            [FromServices] IEarlyWarningNotificationService notificationService,
            [FromQuery] DateOnly? date = null,
            [FromQuery] bool notify = true)
        {
            await earlyWarningService.CheckHiringCapacityAsync(date);

            if (notify)
                await notificationService.NotifyPendingHiringWarningsAsync();

            return Ok(new
            {
                message = "EarlyWarnings procesados.",
                notified = notify
            });
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard(
            [FromQuery] int? companyId,
            [FromQuery] int? warehouseId,
            [FromQuery] int days = 30)
        {
            var fromDate = DateTime.UtcNow.AddDays(-days);

            var query = _db.EarlyWarnings
                .AsNoTracking()
                .Where(x => x.CreatedAt >= fromDate);

            if (companyId.HasValue)
                query = query.Where(x => x.CompanyId == companyId.Value);

            if (warehouseId.HasValue)
                query = query.Where(x => x.WarehouseId == warehouseId.Value);

            var warnings = await query.ToListAsync();

            var data = new
            {
                Total = warnings.Count,
                Open = warnings.Count(x => x.Status == "Open"),
                Reviewed = warnings.Count(x => x.Status == "Reviewed"),
                Closed = warnings.Count(x => x.Status == "Closed"),
                Critical = warnings.Count(x => x.Level == EarlyWarningLevels.Critical),
                Warning = warnings.Count(x => x.Level == EarlyWarningLevels.Warning),

                ByType = warnings
                    .GroupBy(x => x.Type)
                    .Select(g => new
                    {
                        Type = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count),

                ByWarehouse = warnings
                    .GroupBy(x => x.WarehouseId)
                    .Select(g => new
                    {
                        WarehouseId = g.Key,
                        Count = g.Count(),
                        Open = g.Count(x => x.Status == "Open"),
                        Critical = g.Count(x => x.Level == EarlyWarningLevels.Critical)
                    })
                    .OrderByDescending(x => x.Count),

                Recent = warnings
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(10)
                    .Select(x => new
                    {
                        x.Id,
                        x.CompanyId,
                        x.WarehouseId,
                        x.Type,
                        x.Level,
                        x.Status,
                        x.ReferenceDate,
                        x.Message,
                        x.CreatedAt
                    })
            };

            return Ok(data);
        }
    }
}