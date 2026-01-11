using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using TToApp.Model;
using TToApp.Services.Scheduled;


namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;
        private readonly WhatsAppService _whatsAppService;

        public ReportController(ApplicationDbContext context, IConfiguration configuration, EmailService emailService, WhatsAppService whatsAppService)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
        }

        // GET: api/employeeByManager/{managerId}
        [HttpGet("employeeByManager/{managerId}")]
        public async Task<IActionResult> GetEmployeeCountByManager(int managerId, string filterType = "month")
        {
            // Buscar el usuario que sea un Manager
            var manager = await _context.Users
                .Where(u => u.Id == managerId && u.UserRole == global::User.Role.Manager)
                .FirstOrDefaultAsync();

            if (manager == null)
            {
                return NotFound(new { message = "Manager no encontrado o no tiene rol de 'Manager'." });
            }

            // Obtener WarehouseId del manager
            int warehouseId = (int)manager.WarehouseId;

            // Contar empleados en el mismo WarehouseId (excluyendo Managers y Admins)
            var employeeCount = await _context.Users
                .Where(u => u.WarehouseId == warehouseId
                            && u.UserRole != global::User.Role.Manager
                            && u.UserRole != global::User.Role.Admin)
                .CountAsync();

            // Obtener todas las ZoneIds que pertenecen a ese WarehouseId
            var zoneIds = await _context.Zones
                .Where(z => z.IdWarehouse == warehouseId)
                .Select(z => z.Id)
                .ToListAsync();

            // Definir el rango de fechas basado en el filtro
            DateTime startDate, endDate;

            if (filterType.ToLower() == "week")
            {
                // Obtener el primer y último día de la semana actual (asumiendo que la semana comienza el lunes)
                DayOfWeek firstDayOfWeek = DayOfWeek.Monday;
                int diff = (7 + (DateTime.UtcNow.DayOfWeek - firstDayOfWeek)) % 7;
                startDate = DateTime.UtcNow.AddDays(-diff).Date;
                endDate = startDate.AddDays(6);
            }
            else // Por defecto, filtrar por mes
            {
                startDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                endDate = startDate.AddMonths(1).AddDays(-1);
            }

            // Consultar métricas dentro del rango de fechas seleccionado
            var totalVolume = await _context.Routes
                .Where(r => zoneIds.Contains((int)r.ZoneId) &&
                            r.Date >= startDate && r.Date <= endDate)
                .SumAsync(r => r.Volumen);

            var totalAttempts = await _context.Routes
                .Where(r => zoneIds.Contains((int)r.ZoneId) &&
                            r.Date >= startDate && r.Date <= endDate)
                .SumAsync(r => r.Attempts);

            var totalCNL = await _context.Routes
                .Where(r => zoneIds.Contains((int)r.ZoneId) &&
                            r.Date >= startDate && r.Date <= endDate)
                .SumAsync(r => r.CNL);

            return Ok(new
            {
                managerId,
                warehouseId,
                employeeCount,
                filterType,
                startDate,
                endDate,
                totalVolume,
                totalAttempts,
                totalCNL
            });
        }

        [HttpGet("driverStatistics/{managerId}/{startDate}/{endDate}")]
        public async Task<IActionResult> GetDriverStatistics(int managerId, string startDate, string endDate)
        {
            if (!DateTime.TryParse(startDate, out DateTime startDateParsed) ||
                !DateTime.TryParse(endDate, out DateTime endDateParsed))
            {
                return BadRequest(new { message = "Formato de fecha inválido. Use YYYY-MM-DD." });
            }

            // Obtener el usuario que está solicitando la información
            var user = await _context.Users
                .Where(u => u.Id == managerId && u.UserRole.HasValue)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return NotFound(new { message = "Usuario no encontrado." });
            }

            // Lista para almacenar los IDs de los conductores
            List<int> driverIds;

            // Si el usuario es Admin, obtiene todos los conductores de todos los almacenes
            if (user.UserRole.Value == global::User.Role.Admin)
            {
                driverIds = await _context.Users
                    .Where(u => u.UserRole.HasValue && u.UserRole.Value == global::User.Role.Driver)
                    .Select(u => u.Id)
                    .ToListAsync();
            }
            else if (user.UserRole.Value == global::User.Role.Manager)
            {
                if (user.WarehouseId == null)
                {
                    return NotFound(new { message = "Manager no tiene un almacén asignado." });
                }

                int warehouseId = user.WarehouseId.Value;

                // Obtener los conductores del almacén del manager
                driverIds = await _context.Users
                    .Where(u => u.WarehouseId == warehouseId && u.UserRole.HasValue && u.UserRole.Value == global::User.Role.Driver)
                    .Select(u => u.Id)
                    .ToListAsync();
            }
            else
            {
                return BadRequest(new { message = "Usuario no autorizado para ver estas estadísticas." });
            }

            if (!driverIds.Any())
            {
                return NotFound(new { message = "No hay conductores disponibles." });
            }

            // Obtener estadísticas de los conductores en el rango de fechas seleccionado
            var driverStatistics = await _context.Routes
                .Include(r => r.User)
                .Where(r => r.UserId.HasValue && driverIds.Contains(r.UserId.Value) &&
                        r.Date.Date >= startDateParsed.Date &&
                        r.Date.Date <= endDateParsed.Date)
                .GroupBy(r => new { r.UserId, r.User.Name, r.User.LastName })
                .Select(group => new
                {
                    DriverId = group.Key.UserId,
                    DriverName = group.Key.Name + " " + group.Key.LastName,
                    TotalVolume = group.Sum(r => r.Volumen),
                    TotalAttempts = group.Sum(r => r.Attempts),
                    TotalStops = group.Sum(r => r.DeliveryStops),
                    TotalOnTimeDeliveries = group.Sum(r => r.CustomerOnTime),
                    TotalBranchOnTime = group.Sum(r => r.BranchOnTime),
                    TotalCNL = group.Sum(r => r.CNL),
                    TotalDaysWorked = group.Where(r => r.Volumen > 0)
                                           .Select(r => r.Date.Date)
                                           .Distinct()
                                           .Count(),
                    SumLOS = group.Sum(r => (double)r.Los),

                    // Cálculo de LOS % promedio por día trabajado
                    AverageLOSPerDay = group.Where(r => r.Volumen > 0)
                                            .Select(r => r.Date.Date)
                                            .Distinct()
                                            .Count() > 0
                        ? Math.Round((double)group.Sum(r => r.Los) / group.Where(r => r.Volumen > 0)
                                                       .Select(r => r.Date.Date)
                                                       .Distinct()
                                                       .Count(), 2)
                        : 0,

                    // Cálculo del promedio de Branch On Time por día trabajado
                    AverageBranchOnTimePerDay = group.Where(r => r.Volumen > 0)
                                                     .Select(r => r.Date.Date)
                                                     .Distinct()
                                                     .Count() > 0
                        ? Math.Round((double)group.Sum(r => r.BranchOnTime) / group.Where(r => r.Volumen > 0)
                                                                  .Select(r => r.Date.Date)
                                                                  .Distinct()
                                                                  .Count(), 2)
                        : 0
                })
                .OrderByDescending(stat => stat.AverageLOSPerDay)
                .ToListAsync();

            if (!driverStatistics.Any())
            {
                return NotFound(new { message = "No se encontraron registros de rutas para los conductores en el rango de fechas seleccionado." });
            }

            return Ok(driverStatistics);
        }


        [HttpGet("warehouseStatistics/{startDate?}/{endDate?}")]
        [HttpGet("warehouseStatistics")]
        public async Task<IActionResult> GetWarehouseStatistics(
    [FromRoute] string? startDate,
    [FromRoute] string? endDate,
    [FromQuery] string? startDateQ,
    [FromQuery] string? endDateQ)
        {
            // Permitir enviar fechas por ruta o por querystring
            startDate ??= startDateQ;
            endDate ??= endDateQ;

            if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
                return BadRequest(new { success = false, message = "You must send startDate and endDate in YYYY-MM-DD format." });

            if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDateParsed) ||
                !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDateParsed))
                return BadRequest(new { success = false, message = "Invalid date format. Use YYYY-MM-DD." });

            if (startDateParsed > endDateParsed)
                return BadRequest(new { success = false, message = "The start date cannot be greater than the end date." });

            // 🔐 Claims
            var userIdStr = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var roleClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return Unauthorized(new { success = false, message = "Unauthenticated or invalid user." });

            try
            {
                // Resolver rol desde claim (nombre o valor numérico del enum)
                global::User.Role? resolvedRole = null;
                if (!string.IsNullOrWhiteSpace(roleClaim))
                {
                    if (Enum.TryParse<global::User.Role>(roleClaim, true, out var byName))
                        resolvedRole = byName;
                    else if (int.TryParse(roleClaim, out var asInt))
                    {
                        try { resolvedRole = (global::User.Role)asInt; } catch { /* ignore */ }
                    }
                }

                // Datos mínimos del usuario
                var userData = await _context.Users
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.CompanyId, u.WarehouseId })
                    .FirstOrDefaultAsync();

                if (userData == null)
                    return Unauthorized(new { success = false, message = "User not found." });

                // ✅ Determinar warehouses permitidos
                HashSet<int> allowedWarehouseIds = new();

                if (resolvedRole == global::User.Role.CompanyOwner)
                {
                    var companyIds = await _context.Companies
                        .Where(c => c.OwnerId == userId)
                        .Select(c => c.Id)
                        .ToListAsync();

                    if (companyIds.Count > 0)
                    {
                        var whIds = await _context.Warehouses
                            .Where(w => w.CompanyId.HasValue && companyIds.Contains(w.CompanyId.Value))
                            .Select(w => w.Id)
                            .ToListAsync();

                        allowedWarehouseIds = whIds.ToHashSet();
                    }
                }
                else if (resolvedRole == global::User.Role.Admin || resolvedRole == global::User.Role.Manager)
                {
                    int? companyId = userData.CompanyId;

                    // Fallback: si no hay CompanyId en Users, inferirlo desde su Warehouse
                    if (!companyId.HasValue && userData.WarehouseId.HasValue)
                    {
                        companyId = await _context.Warehouses
                            .Where(w => w.Id == userData.WarehouseId.Value)
                            .Select(w => w.CompanyId)
                            .FirstOrDefaultAsync();
                    }

                    if (companyId.HasValue)
                    {
                        var whIds = await _context.Warehouses
                            .Where(w => w.CompanyId.HasValue && w.CompanyId.Value == companyId.Value)
                            .Select(w => w.Id)
                            .ToListAsync();

                        allowedWarehouseIds = whIds.ToHashSet();
                    }
                }
                else
                {
                    if (userData.WarehouseId.HasValue)
                        allowedWarehouseIds.Add(userData.WarehouseId.Value);
                }

                // Si no hay almacenes permitidos, devolver 200 con mensaje
                if (allowedWarehouseIds.Count == 0)
                    return Ok(new { success = false, message = "There are no warehouses associated with the user to display statistics.", data = Array.Empty<object>() });

                // 📅 Rango inclusivo
                var startInclusive = startDateParsed.ToDateTime(TimeOnly.MinValue);
                var endExclusive = endDateParsed.AddDays(1).ToDateTime(TimeOnly.MinValue);

                // 📦 Rutas
                var routes = await _context.Routes
                    .AsNoTracking()
                    .Include(r => r.User)
                    .Where(r => r.User != null &&
                                r.User.WarehouseId.HasValue &&
                                allowedWarehouseIds.Contains(r.User.WarehouseId.Value) &&
                                r.Date >= startInclusive &&
                                r.Date < endExclusive && r.routeStatus == RouteStatus.Completed)
                    .ToListAsync();

                var totalDaysInRange = (endDateParsed.DayNumber - startDateParsed.DayNumber) + 1;

                var warehouseStatistics = routes
                    .GroupBy(r => r.User!.WarehouseId!.Value)
                    .Select(group =>
                    {
                        var daysWorked = group
                            .Where(r => r.Volumen > 0)
                            .Select(r => r.Date.Date)
                            .Distinct()
                            .Count();

                        var rspRoutes = group
                            .Where(r => r.User!.UserRole.HasValue && r.User.UserRole == global::User.Role.Rsp)
                            .ToList();

                        double rspAvgBranchOnTimePerDay = 0;
                        double rspAvgLosPerDay = 0;

                        if (rspRoutes.Count > 0 && totalDaysInRange > 0)
                        {
                            var rspTotalBranchOnTime = rspRoutes.Sum(r => (double)r.BranchOnTime);
                            var rspTotalLos = rspRoutes.Sum(r => (double)r.Los);
                            rspAvgBranchOnTimePerDay = Math.Round(rspTotalBranchOnTime / totalDaysInRange, 2);
                            rspAvgLosPerDay = Math.Round(rspTotalLos / totalDaysInRange, 2);
                        }

                        var totalLosAll = group.Sum(r => (double)r.Los);
                        var totalBranchAll = group.Sum(r => (double)r.BranchOnTime);

                        var avgLosPerWorkedDay = daysWorked > 0 ? Math.Round(totalLosAll / daysWorked, 2) : 0;
                        var avgLosPerCalendarDay = totalDaysInRange > 0 ? Math.Round(totalLosAll / totalDaysInRange, 2) : 0;

                        return new
                        {
                            WarehouseId = group.Key,
                            TotalDrivers = group.Select(r => r.UserId).Distinct().Count(),
                            TotalVolume = group.Sum(r => r.Volumen),
                            TotalAttempts = group.Sum(r => r.Attempts),
                            TotalStops = group.Sum(r => r.DeliveryStops),
                            TotalOnTimeDeliveries = group.Sum(r => r.CustomerOnTime),
                            TotalBranchOnTime = totalBranchAll,
                            TotalCNL = group.Sum(r => r.CNL),

                            TotalDaysWorked = daysWorked,
                            TotalDaysInRange = totalDaysInRange,

                            // Claros
                            AvgLOSPerWorkedDay = avgLosPerWorkedDay,
                            AvgLOSPerCalendarDay = avgLosPerCalendarDay,
                            RspAvgBranchOnTimePerDay = rspAvgBranchOnTimePerDay,
                            RspAvgLosPerDay = rspAvgLosPerDay,

                            // Legacy para tu UI actual
                            SumLOS = rspAvgLosPerDay,
                            AverageLOSPerDay = avgLosPerWorkedDay,
                            AverageBranchOnTimePerDay = rspAvgBranchOnTimePerDay
                        };
                    })
                    .OrderByDescending(stat => stat.AverageLOSPerDay)
                    .ToList();

                if (warehouseStatistics.Count == 0)
                    return Ok(new { success = false, message = "No records were found for the selected date range.", data = Array.Empty<object>() });

                return Ok(new { success = true, data = warehouseStatistics });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while processing the request.", error = ex.Message });
            }
        }


        [HttpGet("driverStatistics/{date?}")]
        public async Task<IActionResult> GetDriverStatistics(string? date, [FromQuery] string tz = "Central Standard Time")
        {
            // 0) userId desde el token
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Usuario no autenticado." });

            // 0.1) Obtener CompanyId del usuario logueado (de la BD)
            var companyId = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => (int?)u.CompanyId)
                .FirstOrDefaultAsync();

            if (companyId == null)
                return Unauthorized(new { message = "No se pudo determinar el CompanyId del usuario." });

            // 1) Fecha base: param o ayer (según zona horaria)
            TimeZoneInfo tzInfo;
            try { tzInfo = TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch { tzInfo = TimeZoneInfo.Local; }

            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzInfo);
            DateTime baseDay;
            if (string.IsNullOrWhiteSpace(date))
                baseDay = localNow.Date.AddDays(-1); // default: ayer
            else if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out baseDay))
                return BadRequest(new { message = "Formato de fecha inválido. Use YYYY-MM-DD." });

            var start = baseDay.Date;
            var end = start.AddDays(1);

            // 2) Query filtrando por CompanyId del usuario logueado
            var driverStatistics = await _context.Routes
                .AsNoTracking()
                .Where(r =>
                    r.Date >= start && r.Date < end &&
                    r.User.WarehouseId != null &&
                    r.User.UserRole == global::User.Role.Driver &&
                    r.User.CompanyId == companyId.Value) // 👈 filtro por compañía
                .GroupBy(r => new { r.UserId, r.User.Name, r.User.LastName, r.User.WarehouseId })
                .Select(g => new
                {
                    WarehouseId = g.Key.WarehouseId!.Value,
                    DriverId = g.Key.UserId,
                    DriverName = g.Key.Name + " " + g.Key.LastName,

                    TotalVolume = g.Sum(r => (decimal?)r.Volumen) ?? 0,
                    TotalAttempts = g.Sum(r => (int?)r.Attempts) ?? 0,
                    TotalStops = g.Sum(r => (int?)r.DeliveryStops) ?? 0,
                    TotalOnTimeDeliveries = g.Sum(r => (int?)r.CustomerOnTime) ?? 0,
                    TotalBranchOnTime = g.Sum(r => (decimal?)r.BranchOnTime) ?? 0,
                    TotalCNL = g.Sum(r => (decimal?)r.CNL) ?? 0,
                    TotalDaysWorked = g.Any(r => r.Volumen > 0) ? 1 : 0,
                    SumLOS = g.Sum(r => (decimal?)r.Los) ?? 0m,
                    AverageLOSPerDay = Math.Round((g.Sum(r => (decimal?)r.Los) ?? 0m), 2),
                    AverageBranchOnTimePerDay = Math.Round((g.Sum(r => (decimal?)r.BranchOnTime) ?? 0m), 2),
                    DPOM = (g.Sum(r => (decimal?)r.Volumen) ?? 0m) > 0
                        ? Math.Round((((g.Sum(r => (decimal?)r.CNL) ?? 0m) * 1_000_000m) /
                                      (g.Sum(r => (decimal?)r.Volumen) ?? 1m)), 2)
                        : 0m
                })
                .ToListAsync();

            if (driverStatistics.Count == 0)
                return Ok(Array.Empty<object>());

            // 3) Normalización + score (0..120)
            decimal maxDPOM = driverStatistics.Max(d => d.DPOM) > 0 ? driverStatistics.Max(d => d.DPOM) : 1;
            decimal maxLOS = driverStatistics.Max(d => d.AverageLOSPerDay) > 0 ? driverStatistics.Max(d => d.AverageLOSPerDay) : 1;
            decimal maxBOT = driverStatistics.Max(d => d.AverageBranchOnTimePerDay) > 0 ? driverStatistics.Max(d => d.AverageBranchOnTimePerDay) : 1;

            var withScores = driverStatistics.Select(d => new
            {
                d.WarehouseId,
                d.DriverId,
                d.DriverName,
                d.AverageLOSPerDay,
                d.AverageBranchOnTimePerDay,
                d.DPOM,
                d.TotalVolume,
                Score = Math.Round(
                    (d.DPOM == 0 ? 60m : (1m - (d.DPOM / maxDPOM)) * 60m) +
                    ((maxLOS > 0 ? d.AverageLOSPerDay / maxLOS : 0m) * 36m) +
                    ((maxBOT > 0 ? d.AverageBranchOnTimePerDay / maxBOT : 0m) * 24m),
                    2)
            }).ToList();

            var bestAndWorst = withScores
                .GroupBy(d => d.WarehouseId)
                .Select(g => new
                {
                    WarehouseId = g.Key,
                    BestDriver = g.OrderByDescending(x => x.Score).ThenByDescending(x => x.TotalVolume).FirstOrDefault(),
                    WorstDriver = g.OrderBy(x => x.Score).ThenBy(x => x.TotalVolume).FirstOrDefault()
                })
                .ToList();

            return Ok(bestAndWorst);
        }
        [Authorize]
        [HttpGet("driverStats/{id:int}/{startDate}/{endDate}")]
        public async Task<IActionResult> GetDriverStatsByRangePath(
    int id,
    string startDate,
    string endDate,
    [FromQuery] bool onlyCompleted = true) // opcional (?onlyCompleted=true/false)
        {
            if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDo) ||
                !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDo))
            {
                return BadRequest(new { message = "Formato de fecha inválido. Use YYYY-MM-DD." });
            }
            if (startDo > endDo)
                return BadRequest(new { message = "La fecha inicial no puede ser mayor a la final." });

            var startInclusive = startDo.ToDateTime(TimeOnly.MinValue);
            var endExclusive = endDo.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var query = _context.Routes
                .AsNoTracking()
                .Include(r => r.Zone)
                .Where(r => r.UserId == id &&
                            r.Date >= startInclusive &&
                            r.Date < endExclusive);

            if (onlyCompleted)
                query = query.Where(r => r.routeStatus.HasValue && r.routeStatus.Value == RouteStatus.Completed);

            var rows = await query
                .OrderBy(r => r.Date)
                .Select(r => new
                {
                    // 👇 Campos en camelCase que tu componente usa para filtrar
                    volumen = r.Volumen,
                    attempts = r.Attempts,
                    cnl = r.CNL,
                    zone = new { zoneCode = r.Zone != null ? r.Zone.ZoneCode : null },

                    // 👇 Columnas que pintas en la tabla (PascalCase + date/route)
                    date = r.Date,
                    route = r.Zone != null ? r.Zone.ZoneCode : $"#{r.Id}",
                    TotalVolume = r.Volumen,
                    TotalStops = r.DeliveryStops,
                    TotalAttempts = r.Attempts,
                    TotalCNL = r.CNL,
                    DPOM = r.Volumen > 0
                                          ? Math.Round(((decimal)r.CNL * 1_000_000m) / (decimal)r.Volumen, 2)
                                          : 0m,
                    AverageLOSPerDay = Math.Round((decimal)r.Los, 2)
                })
                .ToListAsync();

            return Ok(rows);
        }

        [HttpGet("warehouseStatistics/currentMonth")]
        public async Task<IActionResult> GetWarehouseStatisticsForCurrentMonth()
        {
            // 1) Usuario logueado
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Usuario no autenticado." });

            // 2) Obtener CompanyId del usuario (si no lo tiene, intentar inferir por su Warehouse)
            var currentUser = await _context.Users
                .Include(u => u.Warehouse)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (currentUser == null)
                return Unauthorized(new { message = "Usuario no encontrado." });

            int? companyId = currentUser.CompanyId;
            if (companyId == null && currentUser.WarehouseId != null)
            {
                companyId = await _context.Warehouses
                    .Where(w => w.Id == currentUser.WarehouseId)
                    .Select(w => (int?)w.CompanyId)
                    .FirstOrDefaultAsync();
            }

            if (companyId == null)
                return Forbid(); // sin compañía asociada, no puede consultar

            // 3) Fechas (mes actual)
            DateTime today = DateTime.Now;
            int currentMonth = today.Month;
            int currentYear = today.Year;

            // 4) Query: solo rutas cuyos almacenes pertenecen a la misma compañía
            var warehouseStatistics = await _context.Routes
                .Include(r => r.User)
                .ThenInclude(u => u.Warehouse)
                .Where(r =>
                    
                    r.User.WarehouseId.HasValue &&
                    r.User.Warehouse.CompanyId == companyId &&   // <-- filtro por compañía
                    r.Date.Year == currentYear &&
                    r.Date.Month == currentMonth)
                .GroupBy(r => new { r.User.WarehouseId, r.User.Warehouse.City })
                .Select(group => new
                {
                    WarehouseId = group.Key.WarehouseId!.Value,
                    WarehouseCity = group.Key.City,
                    TotalVolume = group.Sum(r => r.Volumen)
                })
                .OrderByDescending(stat => stat.TotalVolume)
                .ToListAsync();

            if (!warehouseStatistics.Any())
            {
                return NotFound(new { message = "No se encontraron registros de rutas en el mes actual para su compañía." });
            }

            return Ok(warehouseStatistics);
        }


        [HttpGet("driverIncome/{id:int}")]
        public async Task<IActionResult> GetDriverIncome(int id)
        {
            try
            {
                var completed = (int)RouteStatus.Completed;

                var data = await _context.Routes
                    .AsNoTracking()
                    .Where(r => r.UserId == id
                                && r.Volumen > 1
                                && r.routeStatus.HasValue
                                && (int)r.routeStatus.Value == completed)   // 👈 fuerza routeStatus = 3
                    .Select(r => new
                    {
                        r.Id,
                        r.Date,
                        r.Volumen,
                        r.DeliveryStops,
                        PriceStop = r.Zone != null ? r.Zone.PriceStop : 0m
                    })
                    .GroupBy(r => new { r.Date.Year, r.Date.Month })
                    .Select(g => new
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        TotalIncome = g.Sum(x => x.PriceStop * x.DeliveryStops),
                        TotalStops = g.Sum(x => x.DeliveryStops),
                        Routes = g.Select(x => new
                        {
                            x.Id,
                            x.Date,
                            x.Volumen,
                            x.DeliveryStops,
                            Income = x.PriceStop * x.DeliveryStops
                        }).ToList()
                    })
                    .OrderBy(g => g.Year).ThenBy(g => g.Month)
                    .ToListAsync();

                if (data.Count == 0)
                    return NotFound(new { message = "No hay rutas COMPLETED con Volumen > 1 para este usuario." });

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Ocurrió un error al procesar la solicitud.", error = ex.Message });
            }
        }



        [HttpGet("runPayroll")]
        public async Task<IActionResult> EnviarCorreosResumenAsync(DateTime? startDate, DateTime? endDate)
        {
            var desde = startDate ?? DateTime.Today.AddDays(-8);
            var hasta = endDate ?? DateTime.Today.AddDays(-2);

            string connectionString = _configuration.GetConnectionString("DevConnection");

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
    WITH Datos AS (
        SELECT 
            u.Id AS UserId,
            u.Name,
            u.LastName,
            u.Email,
            CAST(r.[Date] AS DATE) AS Fecha,
            SUM(r.Volumen) AS Volumen,
            SUM(r.DeliveryStops) AS DeliveryStops
        FROM 
            [TToAppDB].[dbo].[Routes] r
        JOIN 
            [TToAppDB].[dbo].[Users] u ON r.UserId = u.Id
        WHERE 
            r.[Date] >= @Desde
            AND r.[Date] <= @Hasta
            AND u.WarehouseId = 5 
            AND r.Volumen > 0 
            AND u.UserRole = 3
        GROUP BY 
            u.Id, u.Name, u.LastName, u.Email, CAST(r.[Date] AS DATE)
    )
    SELECT 
        d.UserId,
        d.Name,
        d.LastName,
        d.Email,
        (
            '<h3>Hola ' + d.Name + ' ' + d.LastName + ', este es tu resumen de la semana:</h3>' +
            '<table border=""1"" cellpadding=""5"" cellspacing=""0"">' +
            '<tr><th>Fecha</th><th>Volumen</th><th>Paradas</th></tr>' +
            (
                SELECT 
                    '<tr><td>' + CONVERT(varchar, Fecha, 23) + '</td><td>' + 
                    CAST(SUM(Volumen) AS varchar) + '</td><td>' + 
                    CAST(SUM(DeliveryStops) AS varchar) + '</td></tr>'
                FROM Datos as d2
                WHERE d2.UserId = d.UserId
                GROUP BY d2.Fecha
                ORDER BY d2.Fecha
                FOR XML PATH(''), TYPE
            ).value('.', 'varchar(max)') +
            (
                SELECT 
                    '<tr style=""font-weight:bold;""><td>Total</td><td>' +
                    CAST(SUM(Volumen) AS varchar) + '</td><td>' +
                    CAST(SUM(DeliveryStops) AS varchar) + '</td></tr>'
                FROM Datos as d3
                WHERE d3.UserId = d.UserId
            ) +
            '</table>'
        ) AS HtmlBody
    FROM Datos d
    GROUP BY d.UserId, d.Name, d.LastName, d.Email;
    ", conn);

            cmd.Parameters.AddWithValue("@Desde", desde);
            cmd.Parameters.AddWithValue("@Hasta", hasta);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string nombre = reader.GetString(1);
                string apellido = reader.GetString(2);
                string email = reader.GetString(3);
                string html = reader.GetString(4);

                try
                {
                    await _emailService.SendEmailAsync(
                        toEmail: email,
                        subject: "Weekly Summary!!",
                        templateFileName: "WeeklySumary.cshtml",
                        placeholders: new Dictionary<string, string> {
                    { "tablaResumen", html }
                        },
                        copy: false
                    );

                    Console.WriteLine($"✅ Enviado a {nombre} {apellido} ({email})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error al enviar a {email}: {ex.Message}");
                }
            }

            return Ok("Correos enviados");
        }

        [HttpPost("send")]
        public IActionResult SendMessage([FromBody] WhatsAppMessageDto dto)
        {
            try
            {
                var sid = _whatsAppService.EnviarMensaje(dto.PhoneNumber, dto.Message);
                return Ok(new { Success = true, Sid = sid });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Error = ex.Message });
            }
        }



    }
    public class WhatsAppMessageDto
    {
        public string PhoneNumber { get; set; } // Solo números con código de país, ej: 521234567890
        public string Message { get; set; }
    }
}


