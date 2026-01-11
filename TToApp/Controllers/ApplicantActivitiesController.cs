using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    public class ApplicantActivitiesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ApplicantActivitiesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/ApplicantActivities
        [HttpGet("{applicantId:long}")]
        [Authorize(Roles = "Admin,Manager,CompanyOwner,Assistant,Recruiter")]
        public async Task<ActionResult<IEnumerable<ApplicantActivity>>> GetApplicantActivityByApplicant(long applicantId)
        {
            var activities = await _context.ApplicantActivity
                .Where(a => a.ApplicantId == applicantId)
                .OrderByDescending(a => a.CreateAt)
                .ToListAsync();

            if (activities == null || activities.Count == 0)
            {
                return NotFound(new { Message = "No activities found for this applicant" });
            }

            return Ok(activities);
        }
        [Authorize]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ApplicantActivityDto>>> List(
    [FromQuery] DateOnly? from = null,
    [FromQuery] DateOnly? to = null)
        {
            // Base con LEFT JOIN a Applicant(User) y Warehouse
            var q =
                from a in _context.ApplicantActivity.AsNoTracking()
                join u in _context.Users.AsNoTracking()
                     on a.ApplicantId equals u.Id into au
                from u in au.DefaultIfEmpty()
                join w in _context.Warehouses.AsNoTracking()
                     on u.WarehouseId equals w.Id into uw
                from w in uw.DefaultIfEmpty()
                select new { a, u, w };

            // Filtros por fecha (CreateAt: DateOnly)
            if (from.HasValue) q = q.Where(x => x.a.CreateAt >= from.Value);
            if (to.HasValue) q = q.Where(x => x.a.CreateAt <= to.Value);

            // Filtro por recruiter=usuario logueado
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (long.TryParse(userIdClaim, out var recruiterId))
                q = q.Where(x => x.a.RecruiterId == recruiterId);

            var list = await q
                .OrderByDescending(x => x.a.activityDate.HasValue)
                .ThenBy(x => x.a.activityDate)
                .ThenBy(x => x.a.CreateAt)
                .Select(x => new ApplicantActivityDto
                {
                    Id = x.a.Id,
                    ApplicantId = x.a.ApplicantId,
                    RecruiterId = x.a.RecruiterId,

                    ApplicantName = (x.u != null
                        ? (string.Concat(x.u.Name ?? "", " ", x.u.LastName ?? "").Trim())
                        : null),

                    WarehouseId = x.u != null ? x.u.WarehouseId : null,
                    WarehouseName = x.w != null
                    ? $"({x.w.Company}) {x.w.City}"
                    : null,
                    Activity = x.a.Activity,
                    Message = x.a.Message,
                    CreateAt = x.a.CreateAt,
                    ActivityDate = x.a.activityDate
                })
                .ToListAsync();

            return Ok(list);
        }
        // GET: api/ApplicantActivities/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ApplicantActivity>> GetApplicantActivity(int id)
        {
            var applicantActivity = await _context.ApplicantActivity.FindAsync(id);

            if (applicantActivity == null)
            {
                return NotFound();
            }

            return applicantActivity;
        }

        // PUT: api/ApplicantActivities/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutApplicantActivity(int id, ApplicantActivity applicantActivity)
        {
            if (id != applicantActivity.Id)
            {
                return BadRequest();
            }

            _context.Entry(applicantActivity).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ApplicantActivityExists(id))
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

        // POST: api/ApplicantActivities
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [Authorize] // <- exige JWT
        [HttpPost]
        public async Task<ActionResult<ApplicantActivity>> PostApplicantActivity([FromBody] ApplicantActivity applicantActivity)
        {
            // 1. Obtener el ID del usuario logueado desde el token.
            //    Normalmente en tu JWT estás guardando el user.Id en el claim NameIdentifier.
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
            {
                // Si por alguna razón no hay claim, es no autorizado
                return Unauthorized(new { message = "User not recognized in token." });
            }

            // Convertir a long/int según tu modelo
            long userId;
            if (!long.TryParse(userIdClaim, out userId))
            {
                return Unauthorized(new { message = "Invalid user id in token." });
            }

            var applicant = _context.Users.FirstOrDefault(u => u.Id == applicantActivity.ApplicantId);
            if(applicant == null)
            {
                return NotFound(new { message = "applicant not found" });
            }
            applicant.RecruiterId = applicantActivity.RecruiterId;

            if (applicantActivity.activityDate.HasValue)
            {
                applicantActivity.activityDate = applicantActivity.activityDate.Value;
            }
            // 2. Forzar que la actividad pertenezca a este usuario.
            //    (No confiamos en lo que venga del body para este campo)
            applicantActivity.RecruiterId = (int)userId;
            applicantActivity.CreateAt = DateOnly.FromDateTime(DateTime.UtcNow); // opcional pero recomendado
           

            // 3. Guardar en DB
            _context.ApplicantActivity.Add(applicantActivity);
            await _context.SaveChangesAsync();

            // 4. Responder
            return CreatedAtAction(
                nameof(GetApplicantActivity),
                new { id = applicantActivity.Id },
                applicantActivity
            );
        }

        // DELETE: api/ApplicantActivities/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteApplicantActivity(int id)
        {
            var applicantActivity = await _context.ApplicantActivity.FindAsync(id);
            if (applicantActivity == null)
            {
                return NotFound();
            }

            _context.ApplicantActivity.Remove(applicantActivity);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ApplicantActivityExists(int id)
        {
            return _context.ApplicantActivity.Any(e => e.Id == id);
        }

        public sealed class ApplicantActivityDto
        {
            public int Id { get; set; }
            public int ApplicantId { get; set; }
            public int RecruiterId { get; set; }
            public string? ApplicantName { get; set; }

            public int? WarehouseId { get; set; }
            public string? WarehouseName { get; set; }
            public string? WarehouseAddress { get; set; }

            public TToApp.Model.ApplicantActivity.ActivityType Activity { get; set; }
            public string Message { get; set; } = "";
            public DateOnly CreateAt { get; set; }
            public DateTimeOffset? ActivityDate { get; set; } // <- tu activityDate
        }
    }
}
