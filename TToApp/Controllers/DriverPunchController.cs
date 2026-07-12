using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq;
using TToApp.Helpers;
using TToApp.Model;

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
            //     return BadRequest(new { message = "GPS accuracy too low. Please try again.", accuracy = req.AccuracyMeters });

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

            // 4) ✅ Reglas del día basadas en Chicago (Local day -> UTC range)
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"); }
            catch { tz = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"); } // fallback Windows

            var serverNowUtc = DateTime.UtcNow;

            var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(serverNowUtc, tz).Date;
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal.AddDays(1), tz);

            var punchesToday = await _db.DriverPunches
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId
                         && p.DriverId == userId
                         // si quieres que el "día" sea por warehouse también:
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

            // 5) Guardar (UTC)
            var punch = new DriverPunch
            {
                CompanyId = companyId,
                WarehouseId = req.WarehouseId,
                DriverId = userId,
                PunchType = req.PunchType,
                OccurredAtUtc = serverNowUtc,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                AccuracyMeters = req.AccuracyMeters,
                DistanceMeters = distance,
                IsWithinGeofence = within,
                Source = within ? PunchSource.GPS : PunchSource.AdminOverride,
                Notes = isOverride ? req.Notes : null,
                CreatedAtUtc = serverNowUtc
            };

            _db.DriverPunches.Add(punch);
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                punch.Id,
                punch.PunchType,
                OccurredAtUtc = DateTime.SpecifyKind(punch.OccurredAtUtc, DateTimeKind.Utc),

                // ✅ debugging para ver si el server está adelantado
                serverNowUtc,

                // (opcional) útil para debug
                todayLocal,
                startUtc,
                endUtc,

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

        static DateTime? AsUtc(DateTime? dt)
    => dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc) : null;

        [Authorize(Roles = "Admin,Manager")]
        [HttpGet("managers/today")]
        public async Task<ActionResult<List<ManagerPunchRowDto>>> ManagersToday(
     [FromQuery] int warehouseId,
     CancellationToken ct)
        {
            int companyId = GetCompanyId();
            var isAdmin = User.IsInRole("Admin");

            int? warehouseFilter = null;
            if (!isAdmin)
                warehouseFilter = warehouseId > 0 ? warehouseId : (int?)null;

            var nowUtc = DateTime.UtcNow;

            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"); }
            catch { tz = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"); }

            var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz).Date;
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal.AddDays(1), tz);

            // ✅ Roles que quieres incluir en el reporte (punches)
            // Ajusta estos valores a los que tengas en tu enum global::User.Role
            var allowedRoles = new List<global::User.Role>
                {
                    global::User.Role.Admin,
                    global::User.Role.Manager,
                    global::User.Role.Assistant,
                    global::User.Role.Recruiter
                };

            var usersQ = _db.Users.AsNoTracking()
                .Where(u => u.CompanyId == companyId && u.IsActive)
                .Where(u => u.UserRole.HasValue && allowedRoles.Contains(u.UserRole.Value));


            // Si NO es admin y quieres filtrar la lista por warehouse:
            if (!isAdmin && warehouseFilter.HasValue && warehouseFilter.Value > 0)
            {
                usersQ = usersQ.Where(u =>
                    u.UserRole == global::User.Role.Admin
                    || u.UserWarehouses.Any(uw => uw.WarehouseId == warehouseFilter.Value && uw.IsActive)
                );
            }

            var people = await usersQ
                .Select(u => new
                {
                    u.Id,
                    Name = (u.Name ?? "") + " " + (u.LastName ?? ""),
                    Avatar = u.AvatarUrl
                })
                .ToListAsync(ct);

            if (people.Count == 0)
                return Ok(new List<ManagerPunchRowDto>());

            var peopleIds = people.Select(m => m.Id).ToList();

            // ✅ Punches de hoy: Admin ve todos; Manager filtra por warehouse si aplica
            var punchesQ = _db.DriverPunches
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId
                         && peopleIds.Contains(p.DriverId)
                         && p.OccurredAtUtc >= startUtc
                         && p.OccurredAtUtc < endUtc
                         && (p.PunchType == PunchType.Arrival || p.PunchType == PunchType.Departure));

            if (!isAdmin && warehouseFilter.HasValue && warehouseFilter.Value > 0)
                punchesQ = punchesQ.Where(p => p.WarehouseId == warehouseFilter.Value);

            var punches = await punchesQ
                .OrderBy(p => p.OccurredAtUtc)
                .ToListAsync(ct);

            var grouped = punches
                .GroupBy(p => p.DriverId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var firstArrival = g
                            .Where(x => x.PunchType == PunchType.Arrival)
                            .OrderBy(x => x.OccurredAtUtc)
                            .FirstOrDefault();

                        var lastDeparture = g
                            .Where(x => x.PunchType == PunchType.Departure)
                            .OrderByDescending(x => x.OccurredAtUtc)
                            .FirstOrDefault();

                        return new
                        {
                            ArrivalAtUtc = AsUtc(firstArrival?.OccurredAtUtc),
                            DepartureAtUtc = AsUtc(lastDeparture?.OccurredAtUtc),

                            ArrivalLat = firstArrival?.Latitude,
                            ArrivalLng = firstArrival?.Longitude,
                            DepartureLat = lastDeparture?.Latitude,
                            DepartureLng = lastDeparture?.Longitude,

                            ArrivalDistance = firstArrival?.DistanceMeters,
                            DepartureDistance = lastDeparture?.DistanceMeters,

                            PunchWarehouseId = (int?)(firstArrival?.WarehouseId ?? lastDeparture?.WarehouseId)
                        };
                    });

            var result = people.Select(m =>
            {
                grouped.TryGetValue(m.Id, out var p);

                return new ManagerPunchRowDto
                {
                    ManagerId = m.Id,
                    Name = m.Name.Trim(),

                    WarehouseId = p?.PunchWarehouseId ?? (warehouseFilter ?? warehouseId),

                    ArrivalAtUtc = p?.ArrivalAtUtc,
                    DepartureAtUtc = p?.DepartureAtUtc,

                    IsActive = p?.ArrivalAtUtc != null && p?.DepartureAtUtc == null,
                    Avatar = m.Avatar,

                    ArrivalLat = p?.ArrivalLat,
                    ArrivalLng = p?.ArrivalLng,
                    DepartureLat = p?.DepartureLat,
                    DepartureLng = p?.DepartureLng,

                    ArrivalDistanceMeters = p?.ArrivalDistance,
                    DepartureDistanceMeters = p?.DepartureDistance
                };
            })
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToList();

            return Ok(result);
        }




        [Authorize(Roles = "Admin,Manager")]
        [HttpGet("managers/summary")]
        public async Task<ActionResult<ManagerPunchSummaryDto>> ManagersSummary(
    // lo puedes dejar por compatibilidad, pero NO se usa
    CancellationToken ct)
        {
            int companyId = GetCompanyId();

            var nowUtc = DateTime.UtcNow;
            var startUtc = nowUtc.Date;
            var endUtc = startUtc.AddDays(1);

            // ✅ Company-wide: managers + assistants (agrega Admin si quieres incluirlo)
            var peopleQ = _db.Users
                .AsNoTracking()
                .Where(u => u.CompanyId == companyId)
                .Where(u =>
                    u.UserRole == global::User.Role.Manager ||
                    u.UserRole == global::User.Role.Assistant || u.UserRole == global::User.Role.Admin
                // || u.UserRole == global::User.Role.Admin  // <- opcional si quieres contarlos
                );

            var peopleIds = await peopleQ.Select(u => u.Id).ToListAsync(ct);

            if (peopleIds.Count == 0)
                return Ok(new ManagerPunchSummaryDto
                {
                    WarehouseId = 0,        // company-wide
                    DateUtc = startUtc,
                    TotalManagers = 0,
                    Active = 0,
                    PunchedOut = 0,
                    NoPunch = 0,
                    TotalSeconds = 0
                });

            // ✅ Company-wide punches (SIN warehouse filter)
            var punches = await _db.DriverPunches
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId
                         && peopleIds.Contains(p.DriverId)
                         && p.OccurredAtUtc >= startUtc
                         && p.OccurredAtUtc < endUtc
                         && (p.PunchType == PunchType.Arrival || p.PunchType == PunchType.Departure))
                .ToListAsync(ct);

            var byPerson = punches
                .GroupBy(p => p.DriverId)
                .Select(g =>
                {
                    var arr = g.Where(x => x.PunchType == PunchType.Arrival)
                               .OrderBy(x => x.OccurredAtUtc)
                               .FirstOrDefault()?.OccurredAtUtc;

                    // ✅ departure: mejor tomar el último (o el último después del arrival)
                    DateTime? dep = null;
                    if (arr is null)
                    {
                        dep = g.Where(x => x.PunchType == PunchType.Departure)
                               .OrderByDescending(x => x.OccurredAtUtc)
                               .FirstOrDefault()?.OccurredAtUtc;
                    }
                    else
                    {
                        dep = g.Where(x => x.PunchType == PunchType.Departure && x.OccurredAtUtc >= arr.Value)
                               .OrderByDescending(x => x.OccurredAtUtc)
                               .FirstOrDefault()?.OccurredAtUtc;
                    }

                    return new { DriverId = g.Key, Arrival = arr, Departure = dep };
                })
                .ToList();

            int totalPeople = peopleIds.Count;
            int withArrival = byPerson.Count(x => x.Arrival != null);
            int noPunch = totalPeople - withArrival;
            int active = byPerson.Count(x => x.Arrival != null && x.Departure == null);
            int punchedOut = byPerson.Count(x => x.Departure != null);

            long totalSeconds = 0;
            foreach (var m in byPerson)
            {
                if (m.Arrival is null) continue;
                var end = m.Departure ?? nowUtc;
                totalSeconds += (long)Math.Max(0, (end - m.Arrival.Value).TotalSeconds);
            }

            return Ok(new ManagerPunchSummaryDto
            {
                WarehouseId = 0, // ✅ company-wide
                DateUtc = startUtc,
                TotalManagers = totalPeople, // (aquí ahora son managers+assistants)
                Active = active,
                PunchedOut = punchedOut,
                NoPunch = noPunch,
                TotalSeconds = totalSeconds
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("admin/force-punchout")]
        public async Task<ActionResult<ForcePunchOutResultDto>> ForcePunchOutOutsideHours(
           [FromBody] ForcePunchOutRequest req,
           CancellationToken ct)
        {
            int companyId = GetCompanyId();
            var nowUtc = DateTime.UtcNow;

            // TZ Chicago
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"); }
            catch { tz = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"); }

            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);

            // Reglas (defaults razonables)
            var maxShiftHours = req.MaxShiftHours.GetValueOrDefault(12);
            var cutoffLocal = ParseCutoff(req.CutoffLocalTime) ?? new TimeSpan(20, 0, 0); // 8:00 PM local por defecto

            if (string.IsNullOrWhiteSpace(req.Notes))
                return BadRequest(new { message = "Notes are required for AdminOverride." });

            // Ventana de búsqueda (para no escanear infinito):
            // 48h normalmente cubre llegadas de ayer tarde y hoy.
            var windowStartUtc = nowUtc.AddHours(-48);

            // Opcional: filtrar por warehouse
            var punchesQ = _db.DriverPunches.AsNoTracking()
                .Where(p => p.CompanyId == companyId
                         && p.OccurredAtUtc >= windowStartUtc
                         && (p.PunchType == PunchType.Arrival || p.PunchType == PunchType.Departure));

            if (req.WarehouseId.HasValue && req.WarehouseId.Value > 0)
                punchesQ = punchesQ.Where(p => p.WarehouseId == req.WarehouseId.Value);

            // Opcional: solo ciertos usuarios
            if (req.TargetUserIds is { Count: > 0 })
                punchesQ = punchesQ.Where(p => req.TargetUserIds.Contains(p.DriverId));

            var punches = await punchesQ.ToListAsync(ct);

            // Detectar activos: último Arrival que NO tenga Departure después
            var byDriver = punches
                .GroupBy(p => p.DriverId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.OccurredAtUtc).ToList());

            var toClose = new List<(int driverId, int warehouseId, DateTime arrivalUtc)>();

            foreach (var kv in byDriver)
            {
                var driverId = kv.Key;
                var list = kv.Value;

                // último arrival
                var lastArrival = list
                    .Where(x => x.PunchType == PunchType.Arrival)
                    .OrderByDescending(x => x.OccurredAtUtc)
                    .FirstOrDefault();

                if (lastArrival == null) continue;

                // ¿existe departure después de ese arrival?
                var hasDepartureAfter = list.Any(x =>
                    x.PunchType == PunchType.Departure &&
                    x.OccurredAtUtc >= lastArrival.OccurredAtUtc);

                if (hasDepartureAfter) continue; // no está activo

                // Regla "fuera de horario"
                var arrivalLocal = TimeZoneInfo.ConvertTimeFromUtc(lastArrival.OccurredAtUtc, tz);

                var overHours = (nowUtc - lastArrival.OccurredAtUtc).TotalHours >= maxShiftHours;
                var afterCutoff = nowLocal.TimeOfDay >= cutoffLocal;

                // Opcional extra: también podrías exigir que sea el mismo día local del arrival,
                // pero normalmente el "overHours" ya controla eso.
                if (overHours || afterCutoff)
                {
                    toClose.Add((driverId, lastArrival.WarehouseId, lastArrival.OccurredAtUtc));
                }
            }

            if (toClose.Count == 0)
            {
                return Ok(new ForcePunchOutResultDto
                {
                    CompanyId = companyId,
                    NowUtc = nowUtc,
                    NowLocal = nowLocal,
                    CutoffLocalTime = cutoffLocal.ToString(@"hh\:mm"),
                    MaxShiftHours = maxShiftHours,
                    ClosedCount = 0,
                    SkippedCount = byDriver.Count,
                    Closed = new(),
                    Skipped = byDriver.Keys.Select(id => new ForcePunchOutSkippedDto
                    {
                        DriverId = id,
                        Reason = "Not active or not outside hours"
                    }).ToList()
                });
            }

            // Crear punchouts (Departure) como AdminOverride
            // Nota: si te preocupa carrera concurrente, puedes revalidar en DB por driver antes de insertar.
            var created = new List<ForcePunchOutRowDto>();

            foreach (var item in toClose)
            {
                var punch = new DriverPunch
                {
                    CompanyId = companyId,
                    WarehouseId = item.warehouseId,
                    DriverId = item.driverId,
                    PunchType = PunchType.Departure,
                    OccurredAtUtc = nowUtc,

                    // No GPS, es override
                    Latitude = null,
                    Longitude = null,
                    AccuracyMeters = null,
                    DistanceMeters = null,
                    IsWithinGeofence = false,
                    Source = PunchSource.AdminOverride,
                    Notes = req.Notes,
                    CreatedAtUtc = nowUtc
                };

                _db.DriverPunches.Add(punch);

                created.Add(new ForcePunchOutRowDto
                {
                    DriverId = item.driverId,
                    WarehouseId = item.warehouseId,
                    ArrivalAtUtc = item.arrivalUtc,
                    DepartureAtUtc = nowUtc,
                    Reason = (nowUtc - item.arrivalUtc).TotalHours >= maxShiftHours
                        ? $"Over max hours ({maxShiftHours})"
                        : $"After cutoff ({cutoffLocal:hh\\:mm} local)"
                });
            }

            await _db.SaveChangesAsync(ct);

            // Skipped = activos pero no cumplen regla o no activos
            var closedIds = toClose.Select(x => x.driverId).ToHashSet();
            var skipped = byDriver.Keys
                .Where(id => !closedIds.Contains(id))
                .Select(id => new ForcePunchOutSkippedDto
                {
                    DriverId = id,
                    Reason = "Not active or not outside hours"
                })
                .ToList();

            return Ok(new ForcePunchOutResultDto
            {
                CompanyId = companyId,
                NowUtc = nowUtc,
                NowLocal = nowLocal,
                CutoffLocalTime = cutoffLocal.ToString(@"hh\:mm"),
                MaxShiftHours = maxShiftHours,
                ClosedCount = created.Count,
                SkippedCount = skipped.Count,
                Closed = created,
                Skipped = skipped
            });
        }

        private static TimeSpan? ParseCutoff(string? cutoff)
        {
            if (string.IsNullOrWhiteSpace(cutoff)) return null;

            // acepta "20:00", "8:00 PM", etc.
            if (TimeSpan.TryParse(cutoff, CultureInfo.InvariantCulture, out var ts))
                return ts;

            if (DateTime.TryParse(cutoff, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
                return dt.TimeOfDay;

            return null;
        }
    }

    // -------------------------
    // DTOs
    // -------------------------
    public class ForcePunchOutRequest
    {
        public int? WarehouseId { get; set; } // null/0 = todos
        public List<int>? TargetUserIds { get; set; } // null/vacío = todos los encontrados
        public int? MaxShiftHours { get; set; } // default 12
        public string? CutoffLocalTime { get; set; } // default 20:00 (Chicago)
        public string Notes { get; set; } = ""; // requerido
    }

    public class ForcePunchOutResultDto
    {
        public int CompanyId { get; set; }
        public DateTime NowUtc { get; set; }
        public DateTime NowLocal { get; set; }
        public string CutoffLocalTime { get; set; } = "";
        public int MaxShiftHours { get; set; }

        public int ClosedCount { get; set; }
        public int SkippedCount { get; set; }

        public List<ForcePunchOutRowDto> Closed { get; set; } = new();
        public List<ForcePunchOutSkippedDto> Skipped { get; set; } = new();
    }

    public class ForcePunchOutRowDto
    {
        public int DriverId { get; set; }
        public int WarehouseId { get; set; }
        public DateTime ArrivalAtUtc { get; set; }
        public DateTime DepartureAtUtc { get; set; }
        public string Reason { get; set; } = "";
    }

    public class ForcePunchOutSkippedDto
    {
        public int DriverId { get; set; }
        public string Reason { get; set; } = "";
    }
}
    public class ManagerPunchRowDto
    {
        public int ManagerId { get; set; }
        public string Name { get; set; } = "";
        public int WarehouseId { get; set; }

        public DateTime? ArrivalAtUtc { get; set; }
        public DateTime? DepartureAtUtc { get; set; }
        public string Avatar { get; set; }
        public bool IsActive { get; set; }
        public double? ArrivalLat { get; set; }
        public double? ArrivalLng { get; set; }
        public double? DepartureLat { get; set; }
        public double? DepartureLng { get; set; }

        public double? ArrivalDistanceMeters { get; set; }
        public double? DepartureDistanceMeters { get; set; }
    }

    public class ManagerPunchSummaryDto
    {
        public int WarehouseId { get; set; }
        public DateTime? DateUtc { get; set; }

        public int TotalManagers { get; set; }
        public int Active { get; set; }
        public int PunchedOut { get; set; }
        public int NoPunch { get; set; }

        // total combinado (active usa nowUtc como cierre)
        public long TotalSeconds { get; set; }
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



