using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/recruiter")]
    [ApiController]
    [Authorize]
    public class RecruiterController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public RecruiterController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ─── My recruited drivers ────────────────────────────────────────────────

        [HttpGet("me/drivers")]
        public async Task<IActionResult> GetMyDrivers(
            [FromQuery] global::User.HiringStage? stage,
            [FromQuery] bool? isActive,
            CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var recruiterId))
                return Unauthorized(new { message = "Invalid token." });

            return await GetDriversInternal(recruiterId, stage, isActive, ct);
        }

        [HttpGet("{recruiterId:int}/drivers")]
        public async Task<IActionResult> GetDriversByRecruiter(
            int recruiterId,
            [FromQuery] global::User.HiringStage? stage,
            [FromQuery] bool? isActive,
            CancellationToken ct)
            => await GetDriversInternal(recruiterId, stage, isActive, ct);

        private async Task<IActionResult> GetDriversInternal(
            int recruiterId,
            global::User.HiringStage? stage,
            bool? isActive,
            CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var query = _db.Users
                .AsNoTracking()
                .Include(u => u.Profile)
                .Include(u => u.Warehouse)
                .Where(u => u.RecruiterId == recruiterId);

            if (stage.HasValue)
                query = query.Where(u => u.Stage == stage.Value);

            if (isActive.HasValue)
                query = query.Where(u => u.IsActive == isActive.Value);

            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync(ct);

            var drivers = users.Select(u =>
            {
                var recruitedOn = DateOnly.FromDateTime(u.CreatedAt);
                var daysRecruited = today.DayNumber - recruitedOn.DayNumber;
                var daysToHire = u.InitialDate.HasValue
                    ? (int?)(u.InitialDate.Value.DayNumber - recruitedOn.DayNumber)
                    : null;
                var daysWithCompany = u.InitialDate.HasValue
                    ? (int?)(today.DayNumber - u.InitialDate.Value.DayNumber)
                    : null;

                return new
                {
                    u.Id,
                    u.Name,
                    u.LastName,
                    u.Email,
                    phone = u.Profile?.PhoneNumber,
                    role = u.UserRole?.ToString(),
                    stage = u.Stage?.ToString(),
                    u.IsActive,
                    u.WasContacted,
                    recruitedAt = u.CreatedAt.ToString("yyyy-MM-dd"),
                    initialDate = u.InitialDate?.ToString("yyyy-MM-dd"),
                    confirmationDate = u.ConfirmationDate?.ToString("yyyy-MM-dd"),
                    daysRecruited,
                    daysToHire,
                    daysWithCompany,
                    warehouse = u.Warehouse == null ? null : new
                    {
                        u.Warehouse.Id,
                        u.Warehouse.Name,
                        u.Warehouse.City,
                        u.Warehouse.State
                    }
                };
            }).ToList();

            return Ok(new
            {
                hired     = drivers.Where(d => d.stage == global::User.HiringStage.Hired.ToString()).ToList(),
                rejected  = drivers.Where(d => d.stage == global::User.HiringStage.Rejected.ToString()).ToList(),
                inPipeline = drivers.Where(d =>
                    d.stage != global::User.HiringStage.Hired.ToString() &&
                    d.stage != global::User.HiringStage.Rejected.ToString()).ToList()
            });
        }

        // ─── My summary ──────────────────────────────────────────────────────────

        [HttpGet("me/summary")]
        public async Task<IActionResult> GetMySummary(CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var recruiterId))
                return Unauthorized(new { message = "Invalid token." });

            return await GetSummaryInternal(recruiterId, ct);
        }

        [HttpGet("{recruiterId:int}/summary")]
        public async Task<IActionResult> GetSummaryByRecruiter(int recruiterId, CancellationToken ct)
            => await GetSummaryInternal(recruiterId, ct);

        private async Task<IActionResult> GetSummaryInternal(int recruiterId, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var users = await _db.Users
                .AsNoTracking()
                .Where(u => u.RecruiterId == recruiterId)
                .Select(u => new
                {
                    u.Stage,
                    u.IsActive,
                    u.InitialDate,
                    u.CreatedAt
                })
                .ToListAsync(ct);

            var byStage = users
                .GroupBy(u => u.Stage?.ToString() ?? "Unknown")
                .Select(g => new { stage = g.Key, count = g.Count() })
                .OrderBy(g => g.stage)
                .ToList();

            var hired = users.Where(u => u.Stage == global::User.HiringStage.Hired).ToList();
            var hiredWithDate = hired.Where(u => u.InitialDate.HasValue).ToList();
            var avgDaysToHire = hiredWithDate.Count > 0
                ? Math.Round(hiredWithDate.Average(u =>
                    (double)(u.InitialDate!.Value.DayNumber - DateOnly.FromDateTime(u.CreatedAt).DayNumber)), 1)
                : (double?)null;

            return Ok(new
            {
                total = users.Count,
                active = users.Count(u => u.IsActive),
                inactive = users.Count(u => !u.IsActive),
                hired = hired.Count,
                rejected = users.Count(u => u.Stage == global::User.HiringStage.Rejected),
                inPipeline = users.Count(u =>
                    u.Stage != global::User.HiringStage.Hired &&
                    u.Stage != global::User.HiringStage.Rejected),
                avgDaysToHire,
                byStage
            });
        }

        // ─── Overview across all active recruiters (admin use) ───────────────────

        [HttpGet("overview")]
        public async Task<IActionResult> GetRecruitersOverview(CancellationToken ct)
        {
            var recruiters = await _db.Users
                .AsNoTracking()
                .Where(u => (u.UserRole == global::User.Role.Recruiter || u.UserRole == global::User.Role.Assistant) && u.IsActive)
                .Select(u => new { u.Id, u.Name, u.LastName, u.Email })
                .ToListAsync(ct);

            var recruiterIds = recruiters.Select(r => r.Id).ToList();

            var candidates = await _db.Users
                .AsNoTracking()
                .Where(u => u.RecruiterId != null && recruiterIds.Contains(u.RecruiterId.Value))
                .Select(u => new
                {
                    u.RecruiterId,
                    u.Stage,
                    u.IsActive
                })
                .ToListAsync(ct);

            var result = recruiters.Select(r =>
            {
                var group = candidates.Where(u => u.RecruiterId == r.Id).ToList();
                return new
                {
                    r.Id,
                    r.Name,
                    r.LastName,
                    r.Email,
                    total = group.Count,
                    active = group.Count(u => u.IsActive),
                    hired = group.Count(u => u.Stage == global::User.HiringStage.Hired),
                    inPipeline = group.Count(u =>
                        u.Stage != global::User.HiringStage.Hired &&
                        u.Stage != global::User.HiringStage.Rejected),
                    rejected = group.Count(u => u.Stage == global::User.HiringStage.Rejected)
                };
            }).OrderByDescending(r => r.total).ToList();

            return Ok(new { recruiters = result });
        }
    }
}
