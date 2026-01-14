using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TToApp.Helpers;
using TToApp.Model;
using Microsoft.EntityFrameworkCore;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DriverPunchController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public DriverPunchController(ApplicationDbContext context)
        {
            _db = context;
        }

        [Authorize]
        [HttpGet("today")]
        public async Task<ActionResult<DriverPunchTodayDto>> Today([FromQuery] int warehouseId, CancellationToken ct)
        {
            int userId = GetUserId();
            var nowUtc = DateTime.UtcNow;
            var startUtc = nowUtc.Date;
            var endUtc = startUtc.AddDays(1);

            var punches = await _db.DriverPunches
                .AsNoTracking()
                .Where(p => p.DriverId == userId
                         && p.WarehouseId == warehouseId
                         && p.OccurredAtUtc >= startUtc
                         && p.OccurredAtUtc < endUtc)
                .OrderBy(p => p.OccurredAtUtc)
                .ToListAsync(ct);

            var arrival = punches.FirstOrDefault(p => p.PunchType == PunchType.Arrival);
            var departure = punches.FirstOrDefault(p => p.PunchType == PunchType.Departure);

            return new DriverPunchTodayDto
            {
                HasArrival = arrival != null,
                ArrivalAtUtc = arrival?.OccurredAtUtc,
                HasDeparture = departure != null,
                DepartureAtUtc = departure?.OccurredAtUtc
            };
        }

        // ✅ POST: api/DriverPunch
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Punch([FromBody] DriverPunchRequest req, CancellationToken ct)
        {
            int userId = GetUserId();
            string role = GetUserRole();
            int companyId = GetCompanyId();

            // 1) Warehouse
            var warehouse = await _db.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == req.WarehouseId, ct);

            if (warehouse is null)
                return NotFound(new { message = "Warehouse not found" });

            // Warehouse geo configurado?
            if (warehouse.Latitude is null || warehouse.Longitude is null)
                return BadRequest(new { message = "Warehouse location is not configured" });

            // 2) Accuracy (opcional)
           // if (req.AccuracyMeters is not null && req.AccuracyMeters > 250)
             //   return BadRequest(new { message = "GPS accuracy too low. Please try again.", accuracy = req.AccuracyMeters });

            // 3) Distance + geofence
            var distance = GeoHelper.DistanceInMeters(
                req.Latitude, req.Longitude,
                warehouse.Latitude.Value, warehouse.Longitude.Value);

            var radius = warehouse.GeofenceRadiusMeters <= 0 ? 200 : warehouse.GeofenceRadiusMeters;
            var within = distance <= radius;

            var isDriver = role.Equals("Driver", StringComparison.OrdinalIgnoreCase);

            if (!within && isDriver)
                return BadRequest(new { message = "You are outside the allowed warehouse area", distance, radius });

            var isOverride = !within && !isDriver;
            if (isOverride && string.IsNullOrWhiteSpace(req.Notes))
                return BadRequest(new { message = "Notes are required for AdminOverride." });

            // 4) Reglas del día (UTC)
            var nowUtc = DateTime.UtcNow;
            var startUtc = nowUtc.Date;
            var endUtc = startUtc.AddDays(1);

            var punchesToday = await _db.DriverPunches
                .AsNoTracking()
                .Where(p => p.DriverId == userId
                         && p.WarehouseId == req.WarehouseId
                         && p.OccurredAtUtc >= startUtc
                         && p.OccurredAtUtc < endUtc)
                .ToListAsync(ct);

            bool hasArrival = punchesToday.Any(p => p.PunchType == PunchType.Arrival);
            bool hasDeparture = punchesToday.Any(p => p.PunchType == PunchType.Departure);

            // Secuencia válida
            if (req.PunchType == PunchType.Arrival)
            {
                if (hasArrival && !hasDeparture)
                    return BadRequest(new { message = "Arrival already registered. You must register Departure next." });

                if (hasArrival && hasDeparture)
                    return BadRequest(new { message = "Today already has Arrival and Departure registered." });
            }
            else // Departure
            {
                if (!hasArrival)
                    return BadRequest(new { message = "You must register Arrival before Departure." });

                if (hasDeparture)
                    return BadRequest(new { message = "Departure already registered for today." });
            }

            // 5) Guardar
            var punch = new DriverPunch
            {
                CompanyId = companyId,
                WarehouseId = req.WarehouseId,
                DriverId = userId,
                PunchType = req.PunchType,
                OccurredAtUtc = nowUtc,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                AccuracyMeters = req.AccuracyMeters,
                DistanceMeters = distance,
                IsWithinGeofence = within,
                Source = within ? PunchSource.GPS : PunchSource.AdminOverride,
                Notes = isOverride ? req.Notes : null,
                CreatedAtUtc = nowUtc
            };

            _db.DriverPunches.Add(punch);
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                punch.Id,
                punch.PunchType,
                punch.OccurredAtUtc,
                punch.IsWithinGeofence,
                punch.DistanceMeters,
                radius,
                punch.Source
            });
        }

        // -------------------------
        // Helpers: ajusta a tus claims reales
        // -------------------------
        private int GetUserId()
        {
            // Si ya tienes User.GetUserId(), úsalo y borra esto.
            var v = User.Claims.FirstOrDefault(c =>
                c.Type == "nameid" || c.Type.EndsWith("/nameidentifier"))?.Value;

            return int.TryParse(v, out var id) ? id : throw new UnauthorizedAccessException("Invalid userId claim");
        }

        private string GetUserRole()
        {
            var v = User.Claims.FirstOrDefault(c =>
                c.Type == "role" || c.Type.EndsWith("/role"))?.Value;

            return v ?? "";
        }

        private int GetCompanyId()
        {
            // Ajusta el claim name si el tuyo es diferente (companyId / CompanyId)
            var v = User.Claims.FirstOrDefault(c => c.Type == "companyId" || c.Type == "CompanyId")?.Value;
            return int.TryParse(v, out var id) ? id : 0;
        }



    }

    public class DriverPunchRequest
    {
        public int WarehouseId { get; set; }
        public PunchType PunchType { get; set; } // Arrival | Departure
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? AccuracyMeters { get; set; }
        public string? Notes { get; set; } // requerido si override
    }

    public class DriverPunchTodayDto
    {
        public bool HasArrival { get; set; }
        public DateTime? ArrivalAtUtc { get; set; }
        public bool HasDeparture { get; set; }
        public DateTime? DepartureAtUtc { get; set; }

        public bool IsOpenShift => HasArrival && !HasDeparture;
    }

}
