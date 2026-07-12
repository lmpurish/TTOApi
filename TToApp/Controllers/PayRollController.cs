using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using TToApp.Model;
using TToApp.Services.Payroll;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using PdfTable = iText.Layout.Element.Table;
using PdfCell = iText.Layout.Element.Cell;
using iText.IO.Image;
using iText.Kernel.Colors;
using PdfColor = iText.Kernel.Colors.Color;
using iText.Layout.Borders;
using TToApp.DTOs;
using System.Linq;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PayRollController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PayrollService _service;
        private readonly ILogger<PayRollController> _logger;
        private readonly PayRunApprovedSender _payRunApprovedSender;
        private readonly IWebHostEnvironment _env;
        public PayRollController(ApplicationDbContext db, PayrollService service, ILogger<PayRollController> logger, PayRunApprovedSender sender, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
            _service = service;
            _logger = logger;
            _payRunApprovedSender = sender;
        }

        // -------------------------
        // DTOs
        // -------------------------

        private static readonly HashSet<string> AllowedRateTypes = new(StringComparer.OrdinalIgnoreCase)
    { "PerRoute", "PerStop", "PerPackage", "PerMile", "Hourly", "Mixed" };

        public sealed class ComputePayrollRequest
        {
            public long CompanyId { get; set; }
            public long DriverId { get; set; }
            /// <summary>Formato recomendado: yyyy-MM-dd</summary>
            public string WeekStart { get; set; } = null!;
            /// <summary>Formato recomendado: yyyy-MM-dd</summary>
            public string WeekEnd { get; set; } = null!;
            public long? WarehouseId { get; set; }
            /// <summary>Usuario que ejecuta el cálculo (auditoría)</summary>
            public long UserId { get; set; }
            /// <summary>Opcional: filtrar por ZoneId en Routes</summary>
            public int? ZoneId { get; set; }
        }

        public sealed class CreatePeriodRequest
        {
            public long CompanyId { get; set; }
            public long? WarehouseId { get; set; }
            public string StartDate { get; set; } = null!; // yyyy-MM-dd
            public string EndDate { get; set; } = null!;   // yyyy-MM-dd
            public long UserId { get; set; }
            public string? Notes { get; set; }
        }

        public sealed class PeriodSummaryRow
        {
            public string DriverName {  get; set; }
            public long DriverId { get; set; }
            public decimal Gross { get; set; }
            public decimal Adjustments { get; set; }
            public decimal Net { get; set; }
            public long Run { get; set; }
            public string Status { get; set; }
        }

         public sealed class UserMissingRateDto
        {
            public long UserId { get; set; }
            public string? Name { get; set; }
            public string? LastName { get; set; }
        }

        public sealed class WarehouseNullZoneSummaryDto
        {
            public int WarehouseId { get; set; }
            public Dictionary<string, int> NullZoneRoutesByDate { get; set; } = new();
        }

        public sealed class RoleExceptionSummaryDto
        {
            public int WarehouseId { get; set; }
            public List< string> UserNames { get; set; } = new();
        }

        public sealed class PeriodSummaryDto
        {
            public long PayPeriodId { get; set; }
            public string StartDate { get; set; } = null!;
            public string EndDate { get; set; } = null!;
            public List<PeriodSummaryRow> Drivers { get; set; } = new();
            public List<WarehouseNullZoneSummaryDto> OnTracNullZoneRoutes { get; set; } = new();
            public List<RoleExceptionSummaryDto> RoleExceptionByWarehouse { get; set; } = new();
            public List<UserMissingRateDto> UsersWithOutRate { get; set; } = new();
            public List<DriverStoppedWorkingDto> DriversWhoStoppedWorking { get; set; } = new();
            public decimal TotalNet => Drivers.Sum(d => d.Net);
        }

        // NUEVO: request para materializar un período completo
        public sealed class ComputePeriodRequest
        {
            public long CompanyId { get; set; }
            public long? WarehouseId { get; set; }
            public string StartDate { get; set; } = null!; // yyyy-MM-dd
            public string EndDate { get; set; } = null!; // yyyy-MM-dd
            public long UserId { get; set; }
            public int? ZoneId { get; set; }
            public bool RecalculateAll { get; set; } = false;
        }
        public sealed class GenerateMissingDriverRatesRequest
        {
            public int WarehouseId { get; set; }
            public DateOnly? EffectiveFrom { get; set; } // opcional
            public string RateType { get; set; } = "PerStop"; // default
        }

        // -------------------------
        // Helpers
        // -------------------------
        [HttpPost("periods/compute")]
        public async Task<ActionResult<PeriodSummaryDto>> ComputePeriod([FromBody] ComputePeriodRequest req)
        {
            var start = ParseDateOnly(req.StartDate);
            var end = ParseDateOnly(req.EndDate);
            var endExclusive = end.AddDays(1);

            // 1) Crear/obtener período
            var period = await _db.PayPeriods.FirstOrDefaultAsync(p =>
                p.CompanyId == req.CompanyId &&
                p.WarehouseId == req.WarehouseId &&
                p.StartDate == start &&
                p.EndDate == end
            );

            if (period is null)
            {
                period = new PayPeriod
                {
                    CompanyId = req.CompanyId,
                    WarehouseId = req.WarehouseId,
                    StartDate = start,
                    EndDate = end,
                    Status = "Open",
                    CreatedBy = req.UserId
                };
                _db.PayPeriods.Add(period);
                await _db.SaveChangesAsync();
            }

            // 2) Rutas COMPLETED, STOPS>0 en rango
            var routesQ =
                from r in _db.Set<Routes>().IgnoreQueryFilters().AsNoTracking()
                join z in _db.Set<Zone>().IgnoreQueryFilters().AsNoTracking()
                    on r.ZoneId equals z.Id into zj
                from z in zj.DefaultIfEmpty()
                where r.UserId != null 
                
                      && r.routeStatus == RouteStatus.Completed
                      && r.DeliveryStops > 0
                      && r.Date >= start.ToDateTime(TimeOnly.MinValue)
                      && r.Date < endExclusive.ToDateTime(TimeOnly.MinValue)
                      && (req.WarehouseId.HasValue == false || (int)req.WarehouseId.Value == 0 ||r.WarehouseId == (int)req.WarehouseId.Value)
                      && (req.ZoneId.HasValue == false ||  (int)req.ZoneId.Value == 0 || r.ZoneId == (int)req.ZoneId.Value)
                select new { r, z };

            var driverWarehousePairs = await routesQ
                .Where(x =>
                    x.r.UserId.HasValue &&
                    x.r.WarehouseId.HasValue)
                .Select(x => new
                {
                    DriverId = (long)x.r.UserId!.Value,
                    WarehouseId = x.r.WarehouseId!.Value
                })
                .Distinct()
                .ToListAsync();
                foreach (var pair in driverWarehousePairs)
                {
                    var hasWarehouseRate = await _db.DriverRates
                        .AnyAsync(r =>
                            r.DriverId == pair.DriverId &&
                            r.WarehouseId == pair.WarehouseId);

                    if (!hasWarehouseRate)
                    {
                        await EnsureMissingDriverRatesForWarehouseAsync(
                            warehouseId: pair.WarehouseId,
                            driverId: pair.DriverId,
                            effectiveFrom: start,
                            ct: CancellationToken.None);
                    }
                }

            // Get distinct warehouseIds
            var warehouseIdsAll = await routesQ
                .Select(x => x.r.WarehouseId)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .Distinct()
                .ToListAsync(); // List<int>


            var onTracWarehousesWithNullZone = new List<int>();
            var onTracNullZoneByWarehouse = new List<WarehouseNullZoneSummaryDto>();

            // Si no hay warehouses, no hay nada que procesar
            if (warehouseIdsAll.Count == 0)
            {
                return Ok(new { message = "No warehouses to process " });
            }

            // Clarify if is OnTrac per warehouse
            var onTracWarehouseIds = await _db.Warehouses
                .AsNoTracking()
                .Where(w =>
                    warehouseIdsAll.Contains(w.Id) &&
                    w.CompanyId == req.CompanyId &&
                    (w.Company ?? "").Trim().ToLower() == "ontrac"
                )
                .Select(w => w.Id)
                .ToListAsync();

            // Obtener warehouses OnTrac con rutas que tienen zona null
            if (onTracWarehouseIds.Count > 0)
            {
                onTracWarehousesWithNullZone = await routesQ
                    .Where(x =>
                        x.z == null &&
                        x.r.WarehouseId.HasValue &&
                        onTracWarehouseIds.Contains(x.r.WarehouseId.Value)
                    )
                    .Select(x => x.r.WarehouseId!.Value)
                    .Distinct()
                    .ToListAsync();
            }

            // Resumen por fecha para warehouses con zona null
            if (onTracWarehousesWithNullZone.Count > 0)
            {
                var flat = await routesQ
                    .Where(x =>
                        x.z == null &&
                        x.r.WarehouseId.HasValue &&
                        onTracWarehousesWithNullZone.Contains(x.r.WarehouseId.Value)
                    )
                    .GroupBy(x => new { WarehouseId = x.r.WarehouseId!.Value, Day = x.r.Date.Date })
                    .Select(g => new
                    {
                        g.Key.WarehouseId,
                        Date = g.Key.Day,
                        Count = g.Count()
                    })
                    .ToListAsync();

                onTracNullZoneByWarehouse = flat
                    .GroupBy(x => x.WarehouseId)
                    .Select(g => new WarehouseNullZoneSummaryDto
                    {
                        WarehouseId = g.Key,
                        NullZoneRoutesByDate = g.ToDictionary(
                            x => x.Date.ToString("yyyy-MM-dd"),
                            x => x.Count
                        )
                    })
                    .ToList();

                // Excluir del query principal los warehouses OnTrac con null zone
                routesQ = routesQ.Where(x =>
                    x.r.WarehouseId.HasValue &&
                    !onTracWarehousesWithNullZone.Contains(x.r.WarehouseId.Value)
                );
            }

            var roleFlat = await (
                from rq in routesQ
                join u in _db.Users.AsNoTracking()
                    on rq.r.UserId equals u.Id
                where u.UserRole.HasValue
                    && u.UserRole.Value == global::User.Role.Applicant
                    && rq.r.WarehouseId.HasValue
                select new
                {
                    WarehouseId = rq.r.WarehouseId.Value,
                    //UserId = u.Id,
                    FullName = ((u.Name ?? "") + " " + (u.LastName ?? "")).Trim()
                }
            )
            .Distinct()
            .ToListAsync();

            var roleExceptionByWarehouse = roleFlat
                .GroupBy(x => x.WarehouseId)
                .Select(g => new RoleExceptionSummaryDto
                {
                    WarehouseId = g.Key,
                    UserNames = g
                        .Select(x => x.FullName)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n)
                        .ToList()
                })
                .ToList();


           // Warehouses presentes en routesQ  (List<int>)
            var warehouseIdsFiltered = await routesQ
                .Select(x => x.r.WarehouseId)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .Distinct()
                .ToListAsync();

            // if (warehouseIdsFiltered.Count == 0)
            //      return Ok(new { message = "No Users to process" });

      /*      return Ok(new
            {
                debug = "PAYROLL_V62_2026-02-08_ABC",  // cambia esto cada vez
                count = warehouseIdsAll.Count,
                warehouses = warehouseIdsAll,
                start,
                end,
                warehouseReq = req.WarehouseId,
                company = req.CompanyId
            });*/

            // UserIds presentes en routesQ (List<int>)
            var routeUserIdsQ = routesQ
                .Select(rq => rq.r.UserId)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .Distinct();

            // DriverIds (sin Applicants) -> List<long>
            var driverIds = await (
                from uid in routeUserIdsQ
                join u in _db.Users.AsNoTracking()
                    on uid equals u.Id
                where u.UserRole.HasValue
                && u.UserRole.Value != global::User.Role.Applicant && u.UserRole.Value != global::User.Role.Rsp
                select (long)u.Id
            )
            .Distinct()
            .ToListAsync();

            // Special users (activos, no Applicant, no Driver, y warehouse dentro de los de routesQ)
            var specialUserIds = await _db.Users
                .AsNoTracking()
                .Where(u =>
                    u.IsActive &&
                    u.UserRole.HasValue &&
                    u.UserRole.Value != global::User.Role.Applicant &&
                    u.UserRole.Value != global::User.Role.Driver &&
                    u.UserRole.Value != global::User.Role.Rsp &&
                    u.WarehouseId.HasValue &&
                    warehouseIdsFiltered.Contains(u.WarehouseId.Value)
                )
                .Select(u => (long)u.Id)
                .ToListAsync();

            // 2) Candidates: union en memoria
            var candidateIds = driverIds
                .Union(specialUserIds)
                .Distinct()
                .ToList();

            // 3) IDs que SÍ tienen rate (SQL)
            var allUserIdsWithRates = await _db.DriverRates
                .AsNoTracking()
                .Where(r => candidateIds.Contains(r.DriverId))
                .Select(r => r.DriverId)
                .Distinct()
                .ToListAsync();

            // 4) Usuarios que NO tienen rate + Name/LastName (LEFT JOIN en SQL)
            var usersWithoutRates = await (
                from u in _db.Users.AsNoTracking()
                where candidateIds.Contains((long)u.Id)
                join r in _db.DriverRates.AsNoTracking()
                    on (long)u.Id equals r.DriverId into rr
                where !rr.Any()
                select new UserMissingRateDto
                {
                    UserId = (long)u.Id,
                    Name = u.Name,
                    LastName = u.LastName
                }
            ).ToListAsync();

            // 3) Evitar recalcular si ya existe (a menos que se pida)
            HashSet<int> already = new();
            if (!req.RecalculateAll)
            {
                already = (await _db.PayRuns.Where(x => x.PayPeriodId == period.Id)
                    .Select(x => x.DriverId)
                    .ToListAsync()).ToHashSet();
            }
            // 4) Calcular por driver
            foreach (var driverId in allUserIdsWithRates)
            {
                if (!req.RecalculateAll && already.Contains((int)driverId)) continue;

                try
                {
                    await _service.ComputeDriverWeeklyAsync(
                        companyId: req.CompanyId,
                        driverId: driverId,
                        weekStart: start,
                        weekEnd: end,
                        warehouseId: req.WarehouseId,
                        userId: req.UserId,
                        filterZoneId: req.ZoneId
                    );
                }
                catch (Exception ex)
                {
                    return BadRequest(new
                    {
                        message = "ComputeDriverWeeklyAsync falló",
                        driverId,
                        error = ex.Message,
                        stack = ex.StackTrace
                    });
                }
            }

            var driversWhoStopped = new List<DriverStoppedWorkingDto>();
            try
            {
                var periodStart = start.ToDateTime(TimeOnly.MinValue);
                var periodEnd   = end.AddDays(1).ToDateTime(TimeOnly.MinValue);

                // Última ruta del warehouse (cualquier driver, cualquier fecha)
                var warehouseLastRouteDate = await _db.Routes
                    .AsNoTracking()
                    .Where(r => r.WarehouseId.HasValue && warehouseIdsFiltered.Contains(r.WarehouseId.Value))
                    .MaxAsync(r => (DateTime?)r.Date);

                if (warehouseLastRouteDate.HasValue)
                {
                    // Drivers con rutas en el período
                    var driverIdsInPeriod = await _db.Routes
                        .AsNoTracking()
                        .Where(r =>
                            r.UserId != null &&
                            r.Date >= periodStart && r.Date < periodEnd &&
                            r.WarehouseId.HasValue &&
                            warehouseIdsFiltered.Contains(r.WarehouseId.Value))
                        .Select(r => r.UserId!.Value)
                        .Distinct()
                        .ToListAsync();

                    var driversInPeriod = await _db.Users
                        .AsNoTracking()
                        .Where(u => u.IsActive && u.UserRole == global::User.Role.Driver && driverIdsInPeriod.Contains(u.Id))
                        .Select(u => new { u.Id, u.Name, u.LastName })
                        .ToListAsync();

                    // Última ruta de cada driver (en cualquier fecha)
                    var driverIntIds = driversInPeriod.Select(d => d.Id).ToList();
                    var lastRouteMap = (await _db.Routes
                        .AsNoTracking()
                        .Where(r => r.UserId != null && driverIntIds.Contains(r.UserId.Value))
                        .GroupBy(r => r.UserId!.Value)
                        .Select(g => new { DriverId = g.Key, LastDate = g.Max(r => r.Date) })
                        .ToListAsync())
                        .ToDictionary(x => x.DriverId, x => x.LastDate);

                    var today = DateTime.UtcNow.Date;
                    var warehouseMax = warehouseLastRouteDate.Value.Date;

                    driversWhoStopped = driversInPeriod
                        .Where(d =>
                            lastRouteMap.TryGetValue(d.Id, out var last) &&
                            last.Date < warehouseMax)
                        .Select(d =>
                        {
                            var last = lastRouteMap[d.Id];
                            return new DriverStoppedWorkingDto
                            {
                                DriverId           = d.Id,
                                DriverName         = $"{d.Name} {d.LastName}".Trim(),
                                LastRouteDate      = last.ToString("yyyy-MM-dd"),
                                DaysSinceLastRoute = (warehouseMax - last.Date).Days
                            };
                        })
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error computing DriversWhoStopped");
            }

            // 6) Summary
            var runs = await (
                from r in _db.PayRuns.AsNoTracking()
                join u in _db.Set<User>().AsNoTracking()
                    on r.DriverId equals u.Id into gj
                from u in gj.DefaultIfEmpty()
                where r.PayPeriodId == period.Id
                select new PeriodSummaryRow
                {
                    DriverId = r.DriverId,
                    DriverName = u != null
                        ? (u.Name + " " + u.LastName).Trim()
                        : null,
                    Gross = r.GrossAmount,
                    Adjustments = r.Adjustments,
                    Net = r.NetAmount
                }
            ).ToListAsync();

            var dto = new PeriodSummaryDto
            {
                PayPeriodId = period.Id,
                StartDate = period.StartDate.ToString("yyyy-MM-dd"),
                EndDate = period.EndDate.ToString("yyyy-MM-dd"),
                Drivers = runs,
                OnTracNullZoneRoutes = onTracNullZoneByWarehouse,
                RoleExceptionByWarehouse = roleExceptionByWarehouse,
                UsersWithOutRate = usersWithoutRates,
                DriversWhoStoppedWorking = driversWhoStopped
            };

            return Ok(dto);
        }

        public sealed class PeriodRouteDebugDto
        {
            public int RouteId { get; set; }
            public DateTime RouteDate { get; set; }

            public int DriverId { get; set; }

            public int? ZoneId { get; set; }
            public string? ZoneName { get; set; }

            public int? WarehouseId { get; set; }

            public int DeliveryStops { get; set; }
            public int Attempts { get; set; }

            public string RouteStatus { get; set; } = null!;

            public double Los { get; set; }
            public double CustomerOnTime { get; set; }
            public double BranchOnTime { get; set; }
        }

        private static DateOnly ParseDateOnly(string value)
        {
            // Exacto yyyy-MM-dd
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var d))
                return d;

            // ISO con hora/zona
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
                return DateOnly.FromDateTime(dto.UtcDateTime);

            // Fallback permisivo
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                return DateOnly.FromDateTime(dt);

            throw new ArgumentException("Fecha inválida. Usa formato yyyy-MM-dd.");
        }

        // -------------------------
        // Endpoints
        // -------------------------

        /// <summary>
        /// Calcula el payroll semanal para un driver, basado en Routes completadas entre WeekStart y WeekEnd (inclusive).
        /// </summary>
        [HttpPost("compute")]
        public async Task<ActionResult<PayRun>> Compute([FromBody] ComputePayrollRequest req)
        {
            var start = ParseDateOnly(req.WeekStart);
            var end = ParseDateOnly(req.WeekEnd);

            try
            {
                var payRun = await _service.ComputeDriverWeeklyAsync(
                    companyId: req.CompanyId,
                    driverId: req.DriverId,
                    weekStart: start,
                    weekEnd: end,
                    warehouseId: req.WarehouseId,
                    userId: req.UserId
                // <-- AHORA pasamos zoneId
                );

                var full = await _db.PayRuns
                    .AsNoTracking()
                    .Include(x => x.Lines)
                    .Include(x => x.AdjustmentsList)
                    .FirstAsync(x => x.Id == payRun.Id);

                return Ok(full);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    error = ex.Message,
                    hint = "Crea un DriverRate para este driver con EffectiveFrom anterior al período (o EffectiveTo null). Usa POST /api/payroll/rates."
                });
            }
        }

        /// <summary>
        /// 
        /// Crea u obtiene un PayPeriod para el rango dado. Devuelve el período resultante.
        /// </summary>
        [HttpPost("periods")]
        public async Task<ActionResult<PayPeriod>> CreateOrGetPeriod([FromBody] CreatePeriodRequest req)
        {
            var start = ParseDateOnly(req.StartDate);
            var end = ParseDateOnly(req.EndDate);

            var period = await _db.PayPeriods.FirstOrDefaultAsync(p =>
                p.CompanyId == req.CompanyId &&
                p.StartDate == start &&
                p.EndDate == end &&
                p.WarehouseId == req.WarehouseId
            );

            if (period is null)
            {
                period = new PayPeriod
                {
                    CompanyId = req.CompanyId,
                    WarehouseId = req.WarehouseId,
                    StartDate = start,
                    EndDate = end,
                    Status = "Open",
                    CreatedBy = req.UserId,
                    Notes = req.Notes
                };
                _db.PayPeriods.Add(period);
                await _db.SaveChangesAsync();
            }

            return Ok(period);
        }

        /// <summary>Bloquea un PayPeriod (status: Open -> Locked).</summary>
        [HttpPost("periods/{id:long}/lock")]
        public async Task<IActionResult> LockPeriod(long id)
        {
            var period = await _db.PayPeriods.FindAsync(id);
            if (period is null) return NotFound("PayRun doesn’t exist");
            if (!string.Equals(period.Status, "Open", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Solo se puede bloquear un período en estado 'Open'.");

            period.Status = "Locked";
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Aprueba un PayRun (status: Draft -> Approved).</summary>
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost("{id}/approve")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ApproveRun(long id)
        {
            var run = await _db.PayRuns.FindAsync(id);

            if (run is null)
                return NotFound(new { message = "PayRun does not exist." });

            if (run.Status == "Approved")
                return BadRequest(new { message = "PayRun is already approved." });

            if (run.Status != "Draft")
                return BadRequest(new { message = "Only draft PayRuns can be approved." });

            var currentUserId = GetCurrentUserId();

            run.Status = "Approved";
            run.ApprovedAt = DateTime.UtcNow;
            run.ApprovedByUserId = currentUserId;

            await _db.SaveChangesAsync();

            // Driver
            var user = await _db.Users
                .AsNoTracking()
                .Select(u => new { u.Id, u.WarehouseId })
                .FirstOrDefaultAsync(u => u.Id == run.DriverId);

            if (user is null)
                return BadRequest(new { message = "Driver does not exist for this PayRun." });

            if (user.WarehouseId is null || user.WarehouseId == 0)
                return NoContent(); // no warehouse -> no envío

            var sendPayroll = await _db.Warehouses
                .AsNoTracking()
                .Where(w => w.Id == user.WarehouseId)
                .Select(w => w.SendPayroll)
                .FirstOrDefaultAsync();

            if (sendPayroll==true)
                await _payRunApprovedSender.SendLatestPayRunLineAsync(run.DriverId);

            return NoContent();
        }



        /// <summary>Devuelve un PayRun con detalle de líneas y ajustes.</summary>
        [HttpGet("runs/{id:long}")]
        public async Task<ActionResult<PayRun>> GetRun(long id)
{
            var run = await _db.PayRuns
                .AsNoTracking()
                .Include(r => r.Lines)
                .Include(r => r.AdjustmentsList)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (run is null)
                return NotFound("PayRun no existe.");

            run.Lines = run.Lines
                .OrderBy(l => l.RouteDate == null)   // null al final
                .ThenBy(l => l.RouteDate)            // ordenar por fecha
                .ThenBy(l => l.SourceId)             // luego por SourceId
                .ThenByDescending(l => l.Id)
                .ToList();

            foreach (var line in run.Lines)
            {
                if (line.RouteDate.HasValue)
                {
                    var date = line.RouteDate.Value.ToString("yyyy-MM-dd");
                    line.Description = $"{line.Description}";
                }
            }

            return Ok(run);
        }

        /// <summary>Resumen por driver dentro de un PayPeriod (Gross/Adjust/Net por PayRun).</summary>
        [HttpGet("periods/{id:long}/summary")]
        public async Task<ActionResult<PeriodSummaryDto>> GetPeriodSummary(long id)
        {
            var period = await _db.PayPeriods.FindAsync(id);
            
            if (period is null) return NotFound("PayPeriod no existe.");

            var runs = await (
                 from r in _db.PayRuns.AsNoTracking()
                 join u in _db.Users.AsNoTracking()
                     on r.DriverId equals u.Id into gj
                 from u in gj.DefaultIfEmpty()   // por si el usuario fue eliminado
                 where r.PayPeriodId == id
                 select new PeriodSummaryRow
                 {
                     DriverId = r.DriverId,
                     DriverName = u != null ? u.Name + " " + u.LastName : "Unknown",
                     Gross = r.GrossAmount,
                     Adjustments = r.Adjustments,
                     Net = r.NetAmount,
                     Run = r.Id,
                     Status = r.Status
                 }
                    ).ToListAsync();

            var dto = new PeriodSummaryDto
            {
                PayPeriodId = id,
                StartDate = period.StartDate.ToString("yyyy-MM-dd"),
                EndDate = period.EndDate.ToString("yyyy-MM-dd"),
                Drivers = runs
            };

            return Ok(dto);
        }

        /// <summary>
        /// Exporta un PayRun en CSV (líneas + ajustes). Parámetro opcional: ?filename=...
        /// </summary>
        [HttpGet("runs/{id:long}/export/csv")]
        public async Task<IActionResult> ExportRun(long id, [FromQuery] string? filename = null)
        {
            var run = await _db.PayRuns
                .AsNoTracking()
                .Include(r => r.Lines)
                .Include(r => r.AdjustmentsList)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (run is null) return NotFound("PayRun no existe.");
            
            var driver = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == run.DriverId)
                .Select(u => new { u.Name, u.LastName })   // ajusta nombres de campos si difieren
                .FirstOrDefaultAsync();

            var driverName = driver != null
                ? $"{driver.Name} {driver.LastName}".Trim()
                : $"ID {run.DriverId}";


            var sb = new StringBuilder();

            sb.AppendLine("TTO Logistics - Pay Statement");
            sb.AppendLine($"Driver: {driverName}");
            sb.AppendLine($"Gross: {run.GrossAmount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Net: {run.NetAmount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine("----------------------------------------------------");
            sb.AppendLine("Category,Description,Qty,Rate,Amount");

            // Earnings (líneas positivas)
            foreach (var l in run.Lines.Where(l => l.Amount > 0)
                                    .OrderBy(l => l.SourceType))
            {
                sb.Append("Earnings,")
                .Append(Escape(l.Description)).Append(',')
                .Append(l.Qty.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(l.Rate.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(l.Amount.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }

            // Deductions (líneas negativas)
            foreach (var l in run.Lines.Where(l => l.Amount < 0)
                                    .OrderBy(l => l.SourceType))
            {
                sb.Append("Deductions,")
                .Append(Escape(l.Description)).Append(',')
                .Append(l.Qty.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(l.Rate.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(l.Amount.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }

            // Ajustes si quieres mostrarlos separados
            foreach (var a in run.AdjustmentsList)
            {
                sb.Append("Adjustments,")
                .Append(Escape(a.Reason)).Append(',')
                .Append("1,")
                .Append(a.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(a.Amount.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }


            // var sb = new StringBuilder();
            // //sb.AppendLine("Section,SourceType,SourceId,Description,Qty,Rate,Amount");
            // sb.AppendLine("Section,SourceType,Description,Qty,Rate,Amount");
            // // Líneas
            // foreach (var l in run.Lines.OrderBy(l => l.SourceType).ThenBy(l => l.Id))
            // {
            //     sb.Append("Lines,")
            //       .Append(Escape(l.SourceType)).Append(',')
            //       //.Append(Escape(l.SourceId)).Append(',')
            //       .Append(Escape(l.Description)).Append(',')
            //       .Append(l.Qty.ToString(CultureInfo.InvariantCulture)).Append(',')
            //       .Append(l.Rate.ToString(CultureInfo.InvariantCulture)).Append(',')
            //       .Append(l.Amount.ToString(CultureInfo.InvariantCulture)).AppendLine();
            // }

            // // Ajustes
            // foreach (var a in run.AdjustmentsList.OrderBy(a => a.Id))
            // {
            //     sb.Append("Adjustments,")
            //       .Append(Escape(a.Type)).Append(',')
            //       ///.Append(Escape(run.Id.ToString())).Append(',')
            //       .Append(Escape(a.Reason)).Append(',')
            //       .Append("1,")
            //       .Append(a.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
            //       .Append(a.Amount.ToString(CultureInfo.InvariantCulture)).AppendLine();
            // }

            // // Totales
            // sb.AppendLine();
            // sb.AppendLine($"Totals,,DriverId,{run.DriverId},Gross,{run.GrossAmount.ToString(CultureInfo.InvariantCulture)},Net,{run.NetAmount.ToString(CultureInfo.InvariantCulture)}");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var name = string.IsNullOrWhiteSpace(filename)
                ? $"payrun_{run.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv"
                : filename.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? filename : filename + ".csv";

            return File(bytes, "text/csv", name);

            static string Escape(string? s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                    return $"\"{s.Replace("\"", "\"\"")}\"";
                return s;
            }
        }
        [HttpGet("payruns/{id:long}/export/pdf")]
        public async Task<IActionResult> ExportRunPdf(long id, [FromQuery] string? filename = null)
        {
            var run = await _db.PayRuns
                .AsNoTracking()
                .Include(r => r.Lines)
                .Include(r => r.AdjustmentsList)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (run is null) return NotFound("PayRun no existe.");

            var period = await _db.PayPeriods
                .AsNoTracking()
                .Where(p => p.Id == run.PayPeriodId)
                .Select(p => new { p.EndDate, p.StartDate })
                .FirstOrDefaultAsync();

            // Week range (si tienes Start/End en PayRun, usa eso; aquí ejemplo con null-safe)
            var weekText = (period.StartDate != null && period.EndDate != null)
                ? $"{period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}"
                : "";

            var data = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == run.DriverId)
                .Select(u => new
                {
                    DriverName = u.Name + " " + u.LastName,
                    CompanyLogo = u.Warehouse.Companie.LogoUrl,
                    CompanyName = u.Warehouse.Companie.Name
                })
                .FirstOrDefaultAsync();
            var driverName = data != null ? data.DriverName : $"ID {run.DriverId}";
            var logoPath = data?.CompanyLogo;
            var companyName = data?.CompanyName ?? "TTO Logistics";

            // Totales user-friendly
            var earningsTotal = run.Lines.Where(x => x.Amount > 0).Sum(x => x.Amount);
            var deductionsTotal = run.Lines.Where(x => x.Amount < 0).Sum(x => x.Amount); // negativo
            var adjustmentsTotal = run.AdjustmentsList.Sum(x => x.Amount);

            using var ms = new MemoryStream();
            using var writer = new PdfWriter(ms);
            using var pdf = new PdfDocument(writer);
            using var doc = new Document(pdf);

            var normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

          Image? logoImg = null;

           if (!string.IsNullOrWhiteSpace(logoPath))
            {
                try
                {
                    // 1) Si es URL absoluta -> descargar bytes
                    if (Uri.TryCreate(logoPath, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        // Mejor si inyectas IHttpClientFactory, pero esto funciona para probar:
                        using var http = new HttpClient();
                        var bytess = await http.GetByteArrayAsync(uri);

                        var imgData = ImageDataFactory.Create(bytess);
                        logoImg = new Image(imgData);
                    }
                    else
                    {
                        // 2) Si es path relativo (ej: /uploads/CompanyLogos/xxx.png) -> buscar en wwwroot
                        var localPath = Path.Combine(_env.WebRootPath, logoPath.TrimStart('/', '\\'));

                        if (System.IO.File.Exists(localPath))
                        {
                            var imgData = ImageDataFactory.Create(localPath);
                            logoImg = new Image(imgData);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PDF-LOGO] No se pudo cargar logo. logoPath={logoPath}. Error: {ex.Message}");
                }
            }

            // Header
            doc.Add(new Paragraph("TTO Logistics - Pay Statement")
                .SetFont(boldFont).SetFontSize(16));

            doc.Add(new Paragraph($"Driver: {driverName}")
                .SetFont(normalFont).SetFontSize(11));

            if (!string.IsNullOrWhiteSpace(weekText))
            {
                doc.Add(new Paragraph($"Period: {weekText}")
                    .SetFont(normalFont).SetFontSize(11));
            }

            doc.Add(new Paragraph(" "));

            // Table
            var table = new PdfTable(UnitValue.CreatePercentArray(new float[] { 18, 52, 10, 10, 10 }))
                .UseAllAvailableWidth();

            void AddHeader(string text) =>
                table.AddHeaderCell(new PdfCell().Add(new Paragraph(text).SetFont(boldFont)));

            AddHeader("Category");
            AddHeader("Description");
            AddHeader("Qty");
            AddHeader("Rate");
            AddHeader("Amount");

            foreach (var l in run.Lines.OrderBy(x => x.Amount < 0).ThenBy(x => x.SourceType).ThenBy(x => x.Id))
            {
                var category = l.Amount >= 0 ? "Earnings" : "Deductions";
                var qtyText = l.Qty % 1 == 0? ((int)l.Qty).ToString(): l.Qty.ToString("0.##", CultureInfo.InvariantCulture);
                var rateValue = Math.Abs(l.Rate);
                var amountValue = l.Amount;

                table.AddCell(new Paragraph(category).SetFont(normalFont));
                table.AddCell(new Paragraph(l.Description ?? "").SetFont(normalFont));
                //table.AddCell(new Paragraph(qtyText).SetFont(normalFont));
                table.AddCell(
                new PdfCell()
                    .Add(new Paragraph(qtyText).SetFont(normalFont))
                    .SetTextAlignment(TextAlignment.RIGHT)
                );

                table.AddCell(
                    new PdfCell()
                        .Add(new Paragraph($"${rateValue:0.00}").SetFont(normalFont))
                        .SetTextAlignment(TextAlignment.RIGHT)
                );

                table.AddCell(
                    new PdfCell()
                        .Add(new Paragraph($"${amountValue:0.00}").SetFont(normalFont))
                        .SetTextAlignment(TextAlignment.RIGHT)
                );

                // table.AddCell(new Paragraph($"${rateValue:0.00}").SetFont(normalFont));
                // table.AddCell(new Paragraph($"${amountValue:0.00}").SetFont(normalFont));
            }

            // (Opcional) Adjustments como líneas aparte
            foreach (var a in run.AdjustmentsList.OrderBy(x => x.Id))
            {
                table.AddCell(new Paragraph("Adjustments").SetFont(normalFont));
                table.AddCell(new Paragraph(a.Reason ?? a.Type ?? "").SetFont(normalFont));
                table.AddCell(new Paragraph("1").SetFont(normalFont));
                table.AddCell(new Paragraph(a.Amount.ToString("0.00", CultureInfo.InvariantCulture)).SetFont(normalFont));
                table.AddCell(new Paragraph(a.Amount.ToString("0.00", CultureInfo.InvariantCulture)).SetFont(normalFont));
            }

            doc.Add(table);

            doc.Add(new Paragraph(" "));
            var summary = new PdfTable(UnitValue.CreatePercentArray(new float[] { 70, 30 }))
                .UseAllAvailableWidth();
            var darkGreen = new DeviceRgb(0, 70, 32); 
            void AddSummaryRow(string label, decimal value, bool bold = false)
            {
                var lf = bold ? boldFont : normalFont;
                
                var color = value < 0 ? ColorConstants.RED :
                                    value > 0 ? darkGreen :
                                    ColorConstants.BLACK;

                // Formato contable
                var abs = Math.Abs(value);
                var money = abs.ToString("C", new CultureInfo("en-US"));
                var formatted = value < 0 ? $"({money})" : money;

                summary.AddCell(new PdfCell()
                    .Add(new Paragraph(label).SetFont(lf))
                    .SetBorder(iText.Layout.Borders.Border.NO_BORDER));

                summary.AddCell(new PdfCell()
                    .Add(new Paragraph(formatted).SetFont(lf).SetFontColor(color))
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetBorder(iText.Layout.Borders.Border.NO_BORDER));
            }
            AddSummaryRow("Total Earnings", earningsTotal);
            AddSummaryRow("Total Deductions", deductionsTotal); // negativo
            AddSummaryRow("Adjustments", adjustmentsTotal);
            AddSummaryRow("Gross", run.GrossAmount, bold: true);
            AddSummaryRow("Net", run.NetAmount, bold: true);

            doc.Add(new Paragraph(" "));

            var netText = run.NetAmount < 0
                ? $"({Math.Abs(run.NetAmount).ToString("C", new CultureInfo("en-US"))})"
                : run.NetAmount.ToString("C", new CultureInfo("en-US"));

            doc.Add(new Paragraph($"NET PAY: {netText}")
                .SetFont(boldFont)
                .SetFontSize(16)
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetFontColor(run.NetAmount < 0 ? ColorConstants.RED : darkGreen));

            doc.Add(summary);
            doc.Close();
            var bytes = ms.ToArray();
            var name = string.IsNullOrWhiteSpace(filename)
                ? $"pay_statement_{run.DriverId}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf"
                : filename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? filename : filename + ".pdf";

            return File(bytes, "application/pdf", name);
        }


       [HttpGet("warehouses/{warehouseId:long}/payperiods/{payPeriodId:long}/payruns/export/pdf/summary-details")]
        public async Task<IActionResult> ExportWarehousePayRunsSummaryAndDetailsPdf(long warehouseId, long payPeriodId, [FromQuery] string? filename = null)
        {
            // 1) Validar payperiod pertenece al warehouse + datos company/logo
           var period = await (
                from p in _db.PayPeriods.AsNoTracking()
                join w in _db.Warehouses.AsNoTracking() on p.WarehouseId equals w.Id
                join c in _db.Companies.AsNoTracking() on w.CompanyId equals c.Id
                where p.Id == payPeriodId && p.WarehouseId == warehouseId
                select new
                {
                    p.StartDate,
                    p.EndDate,
                    CompanyName = c.Name,
                    LogoUrl = c.LogoUrl
                }
            ).FirstOrDefaultAsync();
            var warehouse = await _db.Warehouses
                .Where(w => w.Id == warehouseId)
                .Select(w => new
                {
                    w.City,
                    w.Company
                })
                .FirstOrDefaultAsync();

            if (warehouse == null)
                return NotFound("Warehouse not found");
            if (period is null)
                return NotFound("PayPeriod no existe o no pertenece a ese Warehouse.");

            var periodText = $"{period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}";
            var companyName = period.CompanyName ?? "TTO Logistics";
            var logoPath = period.LogoUrl;

            // 2) PayRuns del período
            var runs = await _db.PayRuns
                .AsNoTracking()
                .Include(r => r.Lines)
                .Include(r => r.AdjustmentsList)
                .Where(r => r.PayPeriodId == payPeriodId)
                .ToListAsync();

            if (runs.Count == 0)
                return NotFound("No hay PayRuns para ese PayPeriod.");

            // 3) Nombres de drivers
            var driverIds = runs.Select(r =>(int) r.DriverId).Distinct().ToList();
            var drivers = await _db.Users
                .AsNoTracking()
                .Where(u => driverIds.Contains(u.Id))
                .Select(u => new { u.Id, DriverName = (u.Name + " " + u.LastName).Trim() })
                .ToDictionaryAsync(x => x.Id, x => x.DriverName);

            // 4) Resumen por driver (para portada + para cada detalle)
            var rows = runs
                .Select(r =>
                {
                    var driverIdInt = (int)r.DriverId;
                    var earnings    = r.Lines.Where(x => x.Amount > 0).Sum(x => x.Amount);
                    var deductions  = r.Lines.Where(x => x.Amount < 0).Sum(x => x.Amount); // negativo
                    var adjustments = r.AdjustmentsList.Sum(x => x.Amount);

                    return new
                    {
                        Run = r,
                        DriverName = drivers.TryGetValue(driverIdInt, out var dn) ? dn : $"Driver #{r.DriverId}",
                        Earnings = earnings,
                        Deductions = deductions,
                        Adjustments = adjustments,
                        Gross = r.GrossAmount,
                        Net = r.NetAmount
                    };
                })
                .OrderBy(x => x.DriverName)
                .ToList();

            // Helpers
            string Money(decimal v)
            {
                var abs = Math.Abs(v);
                var m = abs.ToString("C", new CultureInfo("en-US"));
                return v < 0 ? $"({m})" : m;
            }

            var darkGreen = new DeviceRgb(0, 50, 0);

            // 5) Preparar logo una sola vez (ImageData reusable)
            ImageData? logoData = null;
            if (!string.IsNullOrWhiteSpace(logoPath))
            {
                try
                {
                    if (Uri.TryCreate(logoPath, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        using var http = new HttpClient();
                        var bytes = await http.GetByteArrayAsync(uri);
                        logoData = ImageDataFactory.Create(bytes);
                    }
                    else
                    {
                        var localPath = Path.Combine(_env.WebRootPath, logoPath.TrimStart('/', '\\'));
                        if (System.IO.File.Exists(localPath))
                            logoData = ImageDataFactory.Create(localPath);
                    }
                }
                catch { /* opcional: log */ }
            }

            // 6) Crear PDF
            using var ms = new MemoryStream();
            using var writer = new PdfWriter(ms);
            using var pdf = new PdfDocument(writer);
            using var doc = new Document(pdf);

            var normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

            // ============================
            // PAGE 1: SUMMARY (PORTADA)
            // ============================
            if (logoData != null)
            {
                var logo = new Image(logoData);
                logo.ScaleToFit(120, 60);
                doc.Add(logo);
            }

            doc.Add(new Paragraph($"{companyName} - PayRuns Summary")
                .SetFont(boldFont).SetFontSize(16));

            doc.Add(new Paragraph($"Warehouse: {warehouse.Company} ({warehouse.City})   |   Period: {periodText}")
                .SetFont(normalFont)
                .SetFontSize(11));

            doc.Add(new Paragraph($"Drivers: {rows.Count}   |   Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
                .SetFont(normalFont).SetFontSize(10));

            doc.Add(new Paragraph(" "));

            var summaryTable = new PdfTable(UnitValue.CreatePercentArray(new float[] { 30, 14, 14, 14, 14, 14 }))
                .UseAllAvailableWidth();

            void SH(string t) => summaryTable.AddHeaderCell(new PdfCell().Add(new Paragraph(t).SetFont(boldFont)));

            SH("Driver");
            SH("Earnings");
            SH("Deductions");
            SH("Adjustments");
            SH("Gross");
            SH("Net");

            foreach (var r in rows)
            {
                summaryTable.AddCell(new Paragraph(r.DriverName).SetFont(normalFont));

                summaryTable.AddCell(new PdfCell().Add(new Paragraph(Money(r.Earnings)).SetFont(normalFont).SetFontColor(darkGreen))
                    .SetTextAlignment(TextAlignment.RIGHT));

                summaryTable.AddCell(new PdfCell().Add(new Paragraph(Money(r.Deductions)).SetFont(normalFont).SetFontColor(ColorConstants.RED))
                    .SetTextAlignment(TextAlignment.RIGHT));

                var adjColor = r.Adjustments < 0 ? ColorConstants.RED : r.Adjustments > 0 ? darkGreen : ColorConstants.BLACK;
                summaryTable.AddCell(new PdfCell().Add(new Paragraph(Money(r.Adjustments)).SetFont(normalFont).SetFontColor(adjColor))
                    .SetTextAlignment(TextAlignment.RIGHT));

                summaryTable.AddCell(new PdfCell().Add(new Paragraph(Money(r.Gross)).SetFont(normalFont))
                    .SetTextAlignment(TextAlignment.RIGHT));

                var netColor = r.Net < 0 ? ColorConstants.RED : r.Net > 0 ? darkGreen : ColorConstants.BLACK;
                summaryTable.AddCell(new PdfCell().Add(new Paragraph(Money(r.Net)).SetFont(normalFont).SetFontColor(netColor))
                    .SetTextAlignment(TextAlignment.RIGHT));
            }

            doc.Add(summaryTable);

            // Totales generales en portada
            doc.Add(new Paragraph(" "));
            doc.Add(new Paragraph("Totals (All Drivers)")
                .SetFont(boldFont).SetFontSize(12));

            var totalE = rows.Sum(x => x.Earnings);
            var totalD = rows.Sum(x => x.Deductions);
            var totalA = rows.Sum(x => x.Adjustments);
            var totalG = rows.Sum(x => x.Gross);
            var totalN = rows.Sum(x => x.Net);

            var totalsTable = new PdfTable(UnitValue.CreatePercentArray(new float[] { 70, 30 }))
                .UseAllAvailableWidth();

            void AddTotal(string label, decimal value, PdfColor? color = null)
            {
                totalsTable.AddCell(new PdfCell().Add(new Paragraph(label).SetFont(normalFont))
                    .SetBorder(null));

                var p = new Paragraph(Money(value)).SetFont(boldFont);
                if (color != null) p.SetFontColor(color);

                totalsTable.AddCell(new PdfCell().Add(p)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetBorder(null));
            }

            AddTotal("Total Earnings", totalE, darkGreen);
            AddTotal("Total Deductions", totalD, ColorConstants.RED);
            AddTotal("Total Adjustments", totalA, totalA < 0 ? ColorConstants.RED : totalA > 0 ? darkGreen : ColorConstants.BLACK);
            AddTotal("Total Gross", totalG);
            AddTotal("Total Net", totalN, totalN < 0 ? ColorConstants.RED : totalN > 0 ? darkGreen : ColorConstants.BLACK);

            doc.Add(totalsTable);

            // Saltar a detalles
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

            // ============================
            // DETAILS: 1 PAGE PER DRIVER
            // ============================
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var run = r.Run;

                // Header por driver
                if (logoData != null)
                {
                    var logo = new Image(logoData);
                    logo.ScaleToFit(120, 60);
                    doc.Add(logo);
                }

                doc.Add(new Paragraph($"{companyName} - Pay Statement")
                    .SetFont(boldFont).SetFontSize(16));

                doc.Add(new Paragraph($"Driver: {r.DriverName}")
                    .SetFont(normalFont).SetFontSize(11));

                doc.Add(new Paragraph($"Period: {periodText}")
                    .SetFont(normalFont).SetFontSize(11));

                doc.Add(new Paragraph(" "));

                // Tabla de detalles
                var table = new PdfTable(UnitValue.CreatePercentArray(new float[] { 18, 52, 10, 10, 10 }))
                    .UseAllAvailableWidth();

                void DH(string t) => table.AddHeaderCell(new PdfCell().Add(new Paragraph(t).SetFont(boldFont)));

                DH("Category");
                DH("Description");
                DH("Qty");
                DH("Rate");
                DH("Amount");

                foreach (var l in run.Lines.OrderBy(x => x.Amount < 0).ThenBy(x => x.SourceType).ThenBy(x => x.Id))
                {
                    var category = l.Amount >= 0 ? "Earnings" : "Deductions";
                    var qtyText = l.Qty % 1 == 0 ? ((int)l.Qty).ToString() : l.Qty.ToString("0.##", CultureInfo.InvariantCulture);
                    var rateValue = Math.Abs(l.Rate);
                    var amountValue = l.Amount;

                    table.AddCell(new Paragraph(category).SetFont(normalFont));
                    table.AddCell(new Paragraph(l.Description ?? "").SetFont(normalFont));

                    table.AddCell(new PdfCell().Add(new Paragraph(qtyText).SetFont(normalFont))
                        .SetTextAlignment(TextAlignment.RIGHT));

                    table.AddCell(new PdfCell().Add(new Paragraph($"${rateValue:0.00}").SetFont(normalFont))
                        .SetTextAlignment(TextAlignment.RIGHT));

                    table.AddCell(new PdfCell().Add(new Paragraph($"${amountValue:0.00}").SetFont(normalFont))
                        .SetTextAlignment(TextAlignment.RIGHT));
                }

                foreach (var a in run.AdjustmentsList.OrderBy(x => x.Id))
                {
                    table.AddCell(new Paragraph("Adjustments").SetFont(normalFont));
                    table.AddCell(new Paragraph(a.Reason ?? a.Type ?? "").SetFont(normalFont));
                    table.AddCell(new PdfCell().Add(new Paragraph("1").SetFont(normalFont)).SetTextAlignment(TextAlignment.RIGHT));
                    table.AddCell(new PdfCell().Add(new Paragraph($"${Math.Abs(a.Amount):0.00}").SetFont(normalFont)).SetTextAlignment(TextAlignment.RIGHT));
                    table.AddCell(new PdfCell().Add(new Paragraph($"${a.Amount:0.00}").SetFont(normalFont)).SetTextAlignment(TextAlignment.RIGHT));
                }

                doc.Add(table);

                // Summary + NET PAY por driver
                doc.Add(new Paragraph(" "));

                var summary = new PdfTable(UnitValue.CreatePercentArray(new float[] { 70, 30 }))
                    .UseAllAvailableWidth();

                void AddSummaryRow(string label, decimal value, bool bold = false)
                {
                    var lf = bold ? boldFont : normalFont;
                    var color = value < 0 ? ColorConstants.RED : value > 0 ? darkGreen : ColorConstants.BLACK;

                    summary.AddCell(new PdfCell()
                        .Add(new Paragraph(label).SetFont(lf))
                        .SetBorder(null));

                    summary.AddCell(new PdfCell()
                        .Add(new Paragraph(Money(value)).SetFont(lf).SetFontColor(color))
                        .SetTextAlignment(TextAlignment.RIGHT)
                        .SetBorder(null));
                }

                AddSummaryRow("Total Earnings", r.Earnings);
                AddSummaryRow("Total Deductions", r.Deductions);
                AddSummaryRow("Adjustments", r.Adjustments);
                AddSummaryRow("Gross", r.Gross, bold: true);
                AddSummaryRow("Net", r.Net, bold: true);

                doc.Add(summary);

                doc.Add(new Paragraph(" "));

                var netColor = r.Net < 0 ? ColorConstants.RED : darkGreen;
                doc.Add(new Paragraph($"NET PAY: {Money(r.Net)}")
                    .SetFont(boldFont)
                    .SetFontSize(16)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetFontColor(netColor));

                // Page break para el próximo driver (excepto el último)
                if (i < rows.Count - 1)
                    doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            }

            doc.Close();

            var outName = string.IsNullOrWhiteSpace(filename)
                ? $"warehouse_{warehouseId}_payperiod_{payPeriodId}_summary_details_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf"
                : filename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? filename : filename + ".pdf";

            return File(ms.ToArray(), "application/pdf", outName);
        }

        [HttpPut("driverRates/{id:long}")]
        public async Task<ActionResult<DriverRateDto>> UpdateDriverRate(
       [FromRoute] long id,
       [FromBody] UpdateDriverRateRequest body,
       CancellationToken ct)
        {
            if (body is null || id <= 0 || id != body.Id)
                return BadRequest(new { Message = "Invalid payload or mismatched id." });

            if (string.IsNullOrWhiteSpace(body.RateType) || !AllowedRateTypes.Contains(body.RateType))
                return BadRequest(new { Message = "Invalid rateType." });

            var entity = await _db.Set<DriverRate>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null)
                return NotFound(new { Message = "Driver rate not found." });

            // (Opcional) Validar que Driver existe
            var driverExists = await _db.Set<User>().AnyAsync(u => u.Id == body.DriverId, ct);
            if (!driverExists)
                return BadRequest(new { Message = $"DriverId {body.DriverId} not found." });

            // Validaciones de negocio
            if (body.BaseAmount is < 0) return BadRequest(new { Message = "BaseAmount must be >= 0." });
            if (body.MinPayPerRoute is < 0) return BadRequest(new { Message = "MinPayPerRoute must be >= 0." });
            if (body.OverStopBonusThreshold is < 0) return BadRequest(new { Message = "OverStopBonusThreshold must be >= 0." });
            if (body.OverStopBonusPerStop is < 0) return BadRequest(new { Message = "OverStopBonusPerStop must be >= 0." });
            if (body.FailedStopPenalty is < 0) return BadRequest(new { Message = "FailedStopPenalty must be >= 0." });
            if (body.RescueStopRate is < 0) return BadRequest(new { Message = "RescueStopRate must be >= 0." });
            if (body.NightDeliveryBonus is < 0) return BadRequest(new { Message = "NightDeliveryBonus must be >= 0." });
            if (body.DailyAmount is < 0) return BadRequest(new { Message = "Daily Amount must be >= 0." });
            if (body.ExtraAmount is < 0) return BadRequest(new { Message = "Extra Amount must be >= 0." });
            // Fechas (si vienen)
            var effFrom = body.EffectiveFrom ?? entity.EffectiveFrom;
            var effTo = body.EffectiveTo ?? entity.EffectiveTo;

            if (effTo is not null && effFrom > effTo)
                return BadRequest(new { Message = "EffectiveFrom cannot be greater than EffectiveTo." });

            // 1) Detectar rates que se solapen con el rango que quieres guardar
            var newFrom = effFrom;
            var newTo = effTo ?? DateOnly.MaxValue;

            var overlappingRates = await _db.Set<DriverRate>()
                .Where(r => r.DriverId == body.DriverId && r.Id != entity.Id)
                .Where(r => r.EffectiveFrom <= newTo &&
                            (r.EffectiveTo == null || r.EffectiveTo >= newFrom))
                .OrderBy(r => r.EffectiveFrom)
                .ToListAsync(ct);

            // 2) Auto-cerrar los que estén "antes" del nuevo rate
            //    Regla: si un rate empieza ANTES del newFrom, se recorta para terminar el día anterior
            var cutTo = newFrom.AddDays(-1);

            // Si cutTo queda antes del EffectiveFrom del rate viejo, significa que quieres empezar
            // el nuevo rate el mismo día o antes de que el otro comience -> con DateOnly no puedes partir el día.
            // Aquí decides tu regla de negocio:
            foreach (var r in overlappingRates)
            {
                if (r.EffectiveFrom < newFrom)
                {
                    if (cutTo < r.EffectiveFrom)
                    {
                        return Conflict(new
                        {
                            Message = "Cannot start a new rate on the same day as another rate (DateOnly). Use a later EffectiveFrom."
                        });
                    }

                    r.EffectiveTo = cutTo;
                    r.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    // r.EffectiveFrom >= newFrom: es un rate futuro o que empieza el mismo día
                    // decide política:
                    // A) Bloquear
                    return Conflict(new
                    {
                        Message = "There is a future (or same-day) rate that would overlap. Adjust EffectiveFrom or edit the future rate first."
                    });

                    // B) O si quieres, podrías "empujarlo" o cerrarlo, pero eso ya es más delicado.
                }
            }

            // Mapear (solo si vienen valores)
            entity.DriverId = body.DriverId;                 // si permites mover el rate a otro driver; si no, quita esta línea
            entity.RateType = body.RateType;

            if (body.BaseAmount.HasValue) entity.BaseAmount = body.BaseAmount.Value;
            if (body.MinPayPerRoute.HasValue) entity.MinPayPerRoute = body.MinPayPerRoute;
            if (body.OverStopBonusThreshold.HasValue) entity.OverStopBonusThreshold = body.OverStopBonusThreshold;
            if (body.OverStopBonusPerStop.HasValue) entity.OverStopBonusPerStop = body.OverStopBonusPerStop;
            if (body.FailedStopPenalty.HasValue) entity.FailedStopPenalty = body.FailedStopPenalty;
            if (body.RescueStopRate.HasValue) entity.RescueStopRate = body.RescueStopRate;
            if (body.NightDeliveryBonus.HasValue) entity.NightDeliveryBonus = body.NightDeliveryBonus;
            if (body.DailyAmount.HasValue) entity.DailyAmount = body.DailyAmount.Value;
            if (body.ExtraAmount.HasValue) entity.ExtraAmount = body.ExtraAmount.Value;

            entity.EffectiveFrom = effFrom;
            entity.EffectiveTo = effTo;

            entity.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync(ct);

                // Proyección coherente con tu DriverRateDto
                var dto = await (
                    from r in _db.Set<DriverRate>().AsNoTracking().Where(x => x.Id == entity.Id)
                    join u in _db.Set<User>() on r.DriverId equals u.Id into gj
                    from u in gj.DefaultIfEmpty()
                    select new DriverRateDto
                    {
                        Id = r.Id,
                        DriverId = r.DriverId,
                        DriverName = u != null ? u.Name : null,
                        DriverLastName = u != null ? u.LastName : null,

                        RateType = r.RateType,
                        BaseAmount = r.BaseAmount,
                        MinPayPerRoute = r.MinPayPerRoute,
                        OverStopBonusThreshold = r.OverStopBonusThreshold,
                        OverStopBonusPerStop = r.OverStopBonusPerStop,
                        FailedStopPenalty = r.FailedStopPenalty,
                        RescueStopRate = r.RescueStopRate,
                        NightDeliveryBonus = r.NightDeliveryBonus,
                        EffectiveFrom = r.EffectiveFrom,
                        EffectiveTo = r.EffectiveTo,
                        DailyAmount = r.DailyAmount,
                        ExtraAmount = r.ExtraAmount
                    }
                ).FirstAsync(ct);

                return Ok(dto);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { Message = "Concurrency error. Try again." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating DriverRate {Id}", id);
                return StatusCode(500, new { Message = "Unexpected error." });
            }
        }
        [HttpGet("driverRates")]
        public async Task<ActionResult<IEnumerable<DriverRateDto>>> GetDriverRates(
    [FromQuery] int? warehouseId,
    CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var result = await (
                from r in _db.DriverRates.AsNoTracking()
                join u in _db.Users.AsNoTracking()
                    on r.DriverId equals (long)u.Id
                where u.IsActive
                      && (u.UserRole == global::User.Role.Driver || u.UserRole == global::User.Role.Manager)
                      && r.EffectiveFrom <= today
                      && (r.EffectiveTo == null || r.EffectiveTo >= today)
                      && (!warehouseId.HasValue || warehouseId == 0 || u.UserWarehouses.Any(uw => uw.WarehouseId == warehouseId && uw.IsActive))
                select new DriverRateDto
                {
                    Id = r.Id,
                    DriverId = r.DriverId,
                    DriverName = u.Name,
                    DriverLastName = u.LastName,
                    RateType = r.RateType,
                    BaseAmount = r.BaseAmount,
                    MinPayPerRoute = r.MinPayPerRoute,
                    OverStopBonusThreshold = r.OverStopBonusThreshold,
                    OverStopBonusPerStop = r.OverStopBonusPerStop,
                    FailedStopPenalty = r.FailedStopPenalty,
                    RescueStopRate = r.RescueStopRate,
                    NightDeliveryBonus = r.NightDeliveryBonus,
                    EffectiveFrom = r.EffectiveFrom,
                    EffectiveTo = r.EffectiveTo,
                    DailyAmount = r.DailyAmount,
                    ExtraAmount = r.ExtraAmount,
                    WarehouseId = u.WarehouseId
                }
            )
            .OrderBy(x => x.DriverName)
            .ThenBy(x => x.DriverLastName)
            .ToListAsync(ct);

            return Ok(result);
        }
        [HttpPut("driverRates/bulk")]
        public async Task<IActionResult> BulkUpdateDriverRates([FromBody] List<UpdateDriverRateRequest> items, CancellationToken ct)
        {
            if (items is null || items.Count == 0)
                return BadRequest(new { Message = "Empty payload." });

            var ids = items.Select(i => i.Id).ToList();
            var entities = await _db.Set<DriverRate>().Where(x => ids.Contains(x.Id)).ToListAsync(ct);

            foreach (var it in items)
            {
                if (string.IsNullOrWhiteSpace(it.RateType) || !AllowedRateTypes.Contains(it.RateType))
                    return BadRequest(new { Message = $"Invalid rateType for Id={it.Id}." });

                var e = entities.FirstOrDefault(x => x.Id == it.Id);
                if (e is null) continue;

                // Validaciones básicas (puedes refactorizar a un helper)
                if (it.BaseAmount is < 0 || it.MinPayPerRoute is < 0 || it.OverStopBonusThreshold is < 0 ||
                    it.OverStopBonusPerStop is < 0 || it.FailedStopPenalty is < 0 || it.RescueStopRate is < 0 ||
                    it.NightDeliveryBonus is < 0)
                    return BadRequest(new { Message = $"Negative values not allowed for Id={it.Id}." });

                var effFrom = it.EffectiveFrom ?? e.EffectiveFrom;
                var effTo = it.EffectiveTo ?? e.EffectiveTo;
                if (effTo is not null && effFrom > effTo)
                    return BadRequest(new { Message = $"Invalid effective range for Id={it.Id}." });

                // (Para bulk omitimos validación detallada de solapes; si quieres, puedes pre-cargar por driver y validar)

                e.DriverId = it.DriverId;
                e.RateType = it.RateType;

                if (it.BaseAmount.HasValue) e.BaseAmount = it.BaseAmount.Value;
                if (it.MinPayPerRoute.HasValue) e.MinPayPerRoute = it.MinPayPerRoute;
                if (it.OverStopBonusThreshold.HasValue) e.OverStopBonusThreshold = it.OverStopBonusThreshold;
                if (it.OverStopBonusPerStop.HasValue) e.OverStopBonusPerStop = it.OverStopBonusPerStop;
                if (it.FailedStopPenalty.HasValue) e.FailedStopPenalty = it.FailedStopPenalty;
                if (it.RescueStopRate.HasValue) e.RescueStopRate = it.RescueStopRate;
                if (it.NightDeliveryBonus.HasValue) e.NightDeliveryBonus = it.NightDeliveryBonus;

                e.EffectiveFrom = effFrom;
                e.EffectiveTo = effTo;
                e.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Bulk updated", count = entities.Count });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("generate-missing")]
        public async Task<IActionResult> GenerateMissingDriverRates(
            [FromQuery] int warehouseId,
            CancellationToken ct)
        {
            try
            {
                var createdDriverIds = await EnsureMissingDriverRatesForWarehouseAsync(
                    warehouseId: warehouseId,
                    ct: ct);

                return Ok(new
                {
                    created = createdDriverIds.Count,
                    warehouseId,
                    driverIds = createdDriverIds,
                    message = createdDriverIds.Count == 0
                        ? "No hay drivers sin DriverRate en ese warehouse."
                        : "DriverRates creados correctamente."
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        // [Authorize(Roles = "Admin")]
        // [HttpPost("generate-missing")]
        // public async Task<IActionResult> GenerateMissingDriverRates(
        // [FromQuery] int warehouseId,
        // CancellationToken ct)
        // {
        //     if (warehouseId <= 0)
        //         return BadRequest(new { message = "warehouseId inválido." });

        //     // 1) Traer warehouse y su rate default
        //     var warehouse = await _db.Warehouses
        //         .AsNoTracking()
        //         .FirstOrDefaultAsync(w => w.Id == warehouseId, ct);

        //     if (warehouse is null)
        //         return NotFound(new { message = $"Warehouse {warehouseId} no existe." });

        //     // Ajusta el nombre según tu modelo:
        //     var baseAmount = warehouse.DriveRate; // <-- CAMBIA si tu propiedad se llama diferente

        //     if (baseAmount <= 0)
        //         return BadRequest(new { message = "El warehouse no tiene DriveRate válido (> 0)." });

        //     var today = new DateOnly(2025, 1, 1);

        //     // 2) Obtener drivers del warehouse que NO tienen rate
        //     //    (RoleId=3 según tu mensaje)
        //     var driverIdsWithoutRate = await _db.Users
        //         .AsNoTracking()
        //         .Where(u =>
        //                  (u.UserRole == global::User.Role.Driver ||
        //                  u.UserRole == global::User.Role.Manager)
        //                 && u.WarehouseId == warehouseId && u.IsActive
        //             )
        //         .Where(u => !_db.DriverRates.Any(dr => dr.DriverId == u.Id))
        //         .Select(u => (long)u.Id)
        //         .ToListAsync(ct);

        //     if (driverIdsWithoutRate.Count == 0)
        //     {
        //         return Ok(new
        //         {
        //             created = 0,
        //             message = "No hay drivers sin DriverRate en ese warehouse."
        //         });
        //     }

        //     // 3) Crear DriverRates (bulk)
        //     var now = DateTime.UtcNow;

        //     var newRates = driverIdsWithoutRate.Select(driverId => new DriverRate
        //     {
        //         DriverId = driverId,
        //         RateType = "PerStop",          // o "PerStop" si ese es tu default
        //         BaseAmount = (decimal)baseAmount,        // desde el warehouse
        //         EffectiveFrom = today,
        //         EffectiveTo = null,
        //         UpdatedAt = now,
        //         ExtraAmount = 0,
        //         // opcional: defaults
        //         MinPayPerRoute = null,
        //         OverStopBonusThreshold = null,
        //         OverStopBonusPerStop = null,
        //         FailedStopPenalty = null,
        //         RescueStopRate = null,
        //         NightDeliveryBonus = null
        //     }).ToList();

        //     await _db.DriverRates.AddRangeAsync(newRates, ct);
        //     await _db.SaveChangesAsync(ct);

        //     return Ok(new
        //     {
        //         created = newRates.Count,
        //         warehouseId,
        //         baseAmount,
        //         rateType = "PerStop",
        //         driverIds = driverIdsWithoutRate
        //     });
        // }

        // [HttpGet("latestGrossAmountByWarehouse1")]
        // public async Task<IActionResult> LatestGrossAmountByWarehouse1()
        // {

        //      // 1) Último PayPeriod por Warehouse (solo lo necesario)
        //     var latest = await _db.Set<PayPeriod>()
        //         .AsNoTracking()
        //         .GroupBy(p => p.WarehouseId)
        //         .Select(g => g.OrderByDescending(p => p.Id).Select(p => new
        //         {
        //             PayPeriodId = p.Id,
        //             p.WarehouseId
        //         }).FirstOrDefault())
        //         .ToListAsync();

        //     // Por si acaso (si algún warehouse no tiene payperiod)
        //     latest = latest.Where(x => x != null).ToList()!;

        //     var latestPayPeriodIds = latest.Select(x => x!.PayPeriodId).ToList();

        //     // 2) Suma GrossAmount + Max CalculatedAt por PayPeriodId (solo para los últimos)
        //     var sums = await _db.Set<PayRun>()
        //         .AsNoTracking()
        //         .Where(pr => latestPayPeriodIds.Contains(pr.PayPeriodId))
        //         .GroupBy(pr => pr.PayPeriodId)
        //         .Select(g => new
        //         {
        //             PayPeriodId = g.Key,
        //             GrossAmountTotal = g.Sum(x => x.GrossAmount),
        //             CalculatedAt = g.Max(x => x.CalculatedAt) 
        //         })
        //         .ToListAsync();

        //     // 3) Warehouses (para nombre City + Company)
        //     var warehouseIds = latest.Select(x => x!.WarehouseId).Distinct().ToList();

        //     var warehouses = await _db.Set<Warehouse>()
        //         .AsNoTracking()
        //         .Where(w => warehouseIds.Contains(w.Id))
        //         .Select(w => new
        //         {
        //             w.Id,
        //             Name = (w.Metro.City ?? "") + "(" + (w.Company ?? "") + ")"
        //         })
        //         .ToListAsync();

        //     var whMap = warehouses.ToDictionary(x => (long)x.Id, x => x.Name);
        //     var sumMap = sums.ToDictionary(x => x.PayPeriodId, x => x);

        //     // 4) Armar respuesta final (en memoria) + formato de fecha
        //     var result = latest.Select(x =>
        //     {
        //         var ppId = x!.PayPeriodId;
        //         sumMap.TryGetValue(ppId, out var s);
        //         whMap.TryGetValue(x.WarehouseId!.Value, out var whName);

        //         return new
        //         {
        //             x.WarehouseId,
        //             Warehouse = whName ?? "",
        //             PayPeriodId = ppId,
        //             GrossAmountTotal = s?.GrossAmountTotal ?? 0m,
        //             Date = s?.CalculatedAt?.ToString("MMM dd yyyy", CultureInfo.InvariantCulture)
        //         };
        //     })
        //     .OrderBy(x => x.WarehouseId)
        //     .ToList();

        //     return Ok(result);
        // }
        [HttpGet("latestGrossAmountByWarehouse")]
        public async Task<IActionResult> LatestGrossAmountByWarehouse()
        {
            // 1) Último PayPeriodId por Warehouse, PERO basado en lo que exista en PayRun
            var latestByWarehouse = await (
                from pr in _db.Set<PayRun>().AsNoTracking()
                join pp in _db.Set<PayPeriod>().AsNoTracking()
                    on pr.PayPeriodId equals pp.Id
                group pr by pp.WarehouseId into g
                select new
                {
                    WarehouseId = g.Key,                
                    PayPeriodId = g.Max(x => x.PayPeriodId)
                }
            ).ToListAsync();

            // Quitar warehouses nulos si WarehouseId fuera nullable
            latestByWarehouse = latestByWarehouse
                .Where(x => x.WarehouseId != null)
                .ToList();

            var latestPayPeriodIds = latestByWarehouse.Select(x => x.PayPeriodId).Distinct().ToList();
            var warehouseIds = latestByWarehouse.Select(x => x.WarehouseId!).Distinct().ToList();

            // 2) Suma GrossAmount + Max CalculatedAt para esos PayPeriodId, por Warehouse
            var sums = await (
                from pr in _db.Set<PayRun>().AsNoTracking()
                join pp in _db.Set<PayPeriod>().AsNoTracking()
                    on pr.PayPeriodId equals pp.Id
                where latestPayPeriodIds.Contains(pr.PayPeriodId)
                    && warehouseIds.Contains(pp.WarehouseId!)
                group pr by new
                {
                    pp.WarehouseId,
                    pr.PayPeriodId,
                    pp.StartDate,
                    pp.EndDate
                } into g
                select new
                {
                    WarehouseId = g.Key.WarehouseId,
                    PayPeriodId = g.Key.PayPeriodId,
                    PeriodStartDate = g.Key.StartDate,
                    PeriodEndDate = g.Key.EndDate,
                    GrossAmountTotal = g.Sum(x => x.GrossAmount),
                    CalculatedAt = g.Max(x => x.CalculatedAt)
                }
            ).ToListAsync();

            // 3) Warehouses (City + Company)
            var warehouses = await _db.Set<Warehouse>()
                .AsNoTracking()
                .Where(w => warehouseIds.Contains((long)w.Id)) // ajusta si tu WarehouseId es int
                .Select(w => new
                {
                    Id = (long)w.Id,
                    Name = (w.Metro.City ?? "") + " (" + (w.Company ?? "") + ")"
                })
                .ToListAsync();

            var whMap = warehouses.ToDictionary(x => x.Id, x => x.Name);

            // Mapa de (WarehouseId -> sum record) para el último PayPeriodId por warehouse
            var latestMap = latestByWarehouse.ToDictionary(x => x.WarehouseId!, x => x.PayPeriodId);

            var sumMap = sums.ToDictionary(
                x => new { WarehouseId = x.WarehouseId!, x.PayPeriodId, },
                x => x
            );

            // 4) Armar resultado final (en memoria) + formato de fecha
            var result = latestMap.Select(kvp =>
        {
            var whId = kvp.Key;
            var ppId = kvp.Value;

            whMap.TryGetValue((long)whId!, out var whName);
            sumMap.TryGetValue(new { WarehouseId = whId, PayPeriodId = ppId }, out var s);

            string? dateRange = null;

            if (s != null)
            {
                dateRange =
                    $"{s.PeriodStartDate:MMM dd yyyy} - {s.PeriodEndDate:MMM dd yyyy}";
            }

            return new
            {
                WarehouseId = whId,
                Warehouse = whName ?? "",
                PayPeriodId = ppId,
                GrossAmountTotal = s?.GrossAmountTotal ?? 0m,
                Date = dateRange
            };
        })
        .OrderBy(x => x.WarehouseId)
        .ToList();

            return Ok(result);
        }

        //Esto es para evitar que amilkar lo rompa
        private int GetCurrentUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        [HttpGet("periods/by-range")]
public async Task<ActionResult<PeriodSummaryDto>> GetPeriodSummaryByRange(
    [FromQuery] long companyId,
    [FromQuery] long? warehouseId,
    [FromQuery] string startDate,
    [FromQuery] string endDate)
{
    var start = ParseDateOnly(startDate);
    var end = ParseDateOnly(endDate);
    var endExclusive = end.AddDays(1);

    var period = await _db.PayPeriods
        .AsNoTracking()
        .FirstOrDefaultAsync(p =>
            p.CompanyId == companyId &&
            p.WarehouseId == warehouseId &&
            p.StartDate == start &&
            p.EndDate == end);

    if (period is null)
        return NotFound("No existing PayPeriod found for that date range.");

    var runs = await (
        from r in _db.PayRuns.AsNoTracking()
        join u in _db.Users.AsNoTracking()
            on r.DriverId equals u.Id into gj
        from u in gj.DefaultIfEmpty()
        where r.PayPeriodId == period.Id
        select new PeriodSummaryRow
        {
            DriverId = r.DriverId,
            DriverName = u != null ? (u.Name + " " + u.LastName).Trim() : "Unknown",
            Gross = r.GrossAmount,
            Adjustments = r.Adjustments,
            Net = r.NetAmount,
            Run = r.Id,
            Status = r.Status
        }
    ).ToListAsync();

    var periodStart = start.ToDateTime(TimeOnly.MinValue);
    var periodEnd = endExclusive.ToDateTime(TimeOnly.MinValue);

    var routesQ =
        from r in _db.Set<Routes>().IgnoreQueryFilters().AsNoTracking()
        join z in _db.Set<Zone>().IgnoreQueryFilters().AsNoTracking()
            on r.ZoneId equals z.Id into zj
        from z in zj.DefaultIfEmpty()
        where r.UserId != null
              && r.routeStatus == RouteStatus.Completed
              && r.DeliveryStops > 0
              && r.Date >= periodStart
              && r.Date < periodEnd
              && (!warehouseId.HasValue || warehouseId.Value == 0 || r.WarehouseId == (int)warehouseId.Value)
        select new { r, z };

    var warehouseIdsFiltered = await routesQ
        .Select(x => x.r.WarehouseId)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .Distinct()
        .ToListAsync();

    var routeUserIdsQ = routesQ
        .Select(x => x.r.UserId)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .Distinct();

    var driverIds = await (
        from uid in routeUserIdsQ
        join u in _db.Users.AsNoTracking()
            on uid equals u.Id
        where u.UserRole.HasValue
              && u.UserRole.Value != global::User.Role.Applicant
              && u.UserRole.Value != global::User.Role.Rsp
        select (long)u.Id
    )
    .Distinct()
    .ToListAsync();

    var specialUserIds = await _db.Users
        .AsNoTracking()
        .Where(u =>
            u.IsActive &&
            u.UserRole.HasValue &&
            u.UserRole.Value != global::User.Role.Applicant &&
            u.UserRole.Value != global::User.Role.Driver &&
            u.UserRole.Value != global::User.Role.Rsp &&
            u.WarehouseId.HasValue &&
            warehouseIdsFiltered.Contains(u.WarehouseId.Value))
        .Select(u => (long)u.Id)
        .ToListAsync();

    var candidateIds = driverIds
        .Union(specialUserIds)
        .Distinct()
        .ToList();

    var usersWithoutRates = await (
        from u in _db.Users.AsNoTracking()
        where candidateIds.Contains((long)u.Id)
        join r in _db.DriverRates.AsNoTracking()
            on (long)u.Id equals r.DriverId into rr
        where !rr.Any()
        select new UserMissingRateDto
        {
            UserId = (long)u.Id,
            Name = u.Name,
            LastName = u.LastName
        }
    ).ToListAsync();

    var roleFlat = await (
        from rq in routesQ
        join u in _db.Users.AsNoTracking()
            on rq.r.UserId equals u.Id
        where u.UserRole.HasValue
              && u.UserRole.Value == global::User.Role.Applicant
              && rq.r.WarehouseId.HasValue
        select new
        {
            WarehouseId = rq.r.WarehouseId.Value,
            FullName = ((u.Name ?? "") + " " + (u.LastName ?? "")).Trim()
        }
    )
    .Distinct()
    .ToListAsync();

    var roleExceptionByWarehouse = roleFlat
        .GroupBy(x => x.WarehouseId)
        .Select(g => new RoleExceptionSummaryDto
        {
            WarehouseId = g.Key,
            UserNames = g
                .Select(x => x.FullName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList()
        })
        .ToList();

    var driversWhoStopped = new List<DriverStoppedWorkingDto>();

    try
    {
        if (warehouseIdsFiltered.Any())
        {
            var warehouseLastRouteDate = await _db.Routes
                .AsNoTracking()
                .Where(r =>
                    r.WarehouseId.HasValue &&
                    warehouseIdsFiltered.Contains(r.WarehouseId.Value))
                .MaxAsync(r => (DateTime?)r.Date);

            if (warehouseLastRouteDate.HasValue)
            {
                var driverIdsInPeriod = await _db.Routes
                    .AsNoTracking()
                    .Where(r =>
                        r.UserId != null &&
                        r.Date >= periodStart &&
                        r.Date < periodEnd &&
                        r.WarehouseId.HasValue &&
                        warehouseIdsFiltered.Contains(r.WarehouseId.Value))
                    .Select(r => r.UserId!.Value)
                    .Distinct()
                    .ToListAsync();

                var driversInPeriod = await _db.Users
                    .AsNoTracking()
                    .Where(u =>
                        u.IsActive &&
                        u.UserRole == global::User.Role.Driver &&
                        driverIdsInPeriod.Contains(u.Id))
                    .Select(u => new { u.Id, u.Name, u.LastName })
                    .ToListAsync();

                var driverIntIds = driversInPeriod.Select(d => d.Id).ToList();

                var lastRouteMap = (await _db.Routes
                    .AsNoTracking()
                    .Where(r =>
                        r.UserId != null &&
                        driverIntIds.Contains(r.UserId.Value))
                    .GroupBy(r => r.UserId!.Value)
                    .Select(g => new
                    {
                        DriverId = g.Key,
                        LastDate = g.Max(r => r.Date)
                    })
                    .ToListAsync())
                    .ToDictionary(x => x.DriverId, x => x.LastDate);

                var warehouseMax = warehouseLastRouteDate.Value.Date;

                driversWhoStopped = driversInPeriod
                    .Where(d =>
                        lastRouteMap.TryGetValue(d.Id, out var last) &&
                        last.Date < warehouseMax)
                    .Select(d =>
                    {
                        var last = lastRouteMap[d.Id];

                        return new DriverStoppedWorkingDto
                        {
                            DriverId = d.Id,
                            DriverName = $"{d.Name} {d.LastName}".Trim(),
                            LastRouteDate = last.ToString("yyyy-MM-dd"),
                            DaysSinceLastRoute = (warehouseMax - last.Date).Days
                        };
                    })
                    .ToList();
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error computing DriversWhoStopped in GetPeriodSummaryByRange");
    }

    var dto = new PeriodSummaryDto
    {
        PayPeriodId = period.Id,
        StartDate = period.StartDate.ToString("yyyy-MM-dd"),
        EndDate = period.EndDate.ToString("yyyy-MM-dd"),
        Drivers = runs,
        UsersWithOutRate = usersWithoutRates,
        DriversWhoStoppedWorking = driversWhoStopped,
        RoleExceptionByWarehouse = roleExceptionByWarehouse
    };

    return Ok(dto);
}
        [HttpPost("runs/{id:long}/adjustments")]
        public async Task<ActionResult> AddAdjustment(long id, [FromBody] CreateAdjustmentRequest req)
        {
            var run = await _db.PayRuns
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (run is null)
                return NotFound("PayRun not found.");

            if (string.Equals(run.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Approved PayRuns cannot be modified." });

            var now = DateTime.UtcNow;

            // 1️⃣ Guardar adjustment
            var adjustment = new PayrollAdjustment
            {
                PayRunId = run.Id,
                Type = req.Type ?? "Manual",
                Reason = req.Reason,
                Amount = req.Amount,
                CreatedAt = now,
                CreatedBy = GetCurrentUserId()
            };

            _db.PayrollAdjustments.Add(adjustment);

            // 2️⃣ Crear línea visible en el PayRun
            var line = new PayRunLine
            {
                PayRunId = run.Id,
                SourceType = "Adjustment",
                SourceId = adjustment.Id.ToString(),
                Description = $"{adjustment.Type} - {adjustment.Reason}",
                Qty = 1,
                Rate = req.Amount,
                Tags = "MANUAL_ADJUSTMENT",
                RouteDate = now
            };

            _db.PayRunLines.Add(line);

            // 3️⃣ Recalcular adjustments
            run.Adjustments = await _db.PayrollAdjustments
                .Where(a => a.PayRunId == run.Id)
                .SumAsync(a => (decimal?)a.Amount) ?? 0m;

            run.NetAmount = run.GrossAmount + run.Adjustments;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Adjustment added successfully.",
                payRunId = run.Id,
                grossAmount = run.GrossAmount,
                adjustments = run.Adjustments,
                netAmount = run.NetAmount
            });
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpDelete("adjustments/{adjustmentId:long}")]
        public async Task<ActionResult> DeleteAdjustment(long adjustmentId)
        {
            var adjustment = await _db.PayrollAdjustments
                .FirstOrDefaultAsync(a => a.Id == adjustmentId);

            if (adjustment is null)
                return NotFound(new { message = "Adjustment not found." });

            var run = await _db.PayRuns
                .FirstOrDefaultAsync(x => x.Id == adjustment.PayRunId);

            if (run is null)
                return NotFound(new { message = "PayRun not found." });

            if (string.Equals(run.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Approved PayRuns cannot be modified." });

            _db.PayrollAdjustments.Remove(adjustment);

            await _db.SaveChangesAsync();

            run.Adjustments = await _db.PayrollAdjustments
                .Where(a => a.PayRunId == run.Id)
                .SumAsync(a => (decimal?)a.Amount) ?? 0m;

            run.NetAmount = run.GrossAmount + run.Adjustments;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Adjustment deleted successfully.",
                payRunId = run.Id,
                grossAmount = run.GrossAmount,
                adjustments = run.Adjustments,
                netAmount = run.NetAmount
            });
        }

       private async Task<List<long>> EnsureMissingDriverRatesForWarehouseAsync(
            int warehouseId,
            long? driverId = null,
            DateOnly? effectiveFrom = null,
            CancellationToken ct = default)
        {
            if (warehouseId <= 0)
                throw new ArgumentException("warehouseId inválido.");

            var warehouse = await _db.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == warehouseId, ct);

            if (warehouse is null)
                throw new InvalidOperationException($"Warehouse {warehouseId} no existe.");

            var baseAmount = warehouse.DriveRate;

            if (baseAmount == null || baseAmount <= 0)
                throw new InvalidOperationException("El warehouse no tiene DriveRate válido (> 0).");

            var effectiveDate = effectiveFrom ?? new DateOnly(2025, 1, 1);

            var driversQuery = _db.Users
                .AsNoTracking()
                .Where(u =>
                    u.IsActive &&
                    (u.UserRole == global::User.Role.Driver ||
                    u.UserRole == global::User.Role.Manager));

            if (driverId.HasValue)
            {
                driversQuery = driversQuery.Where(u => u.Id == driverId.Value);
            }
            else
            {
                driversQuery = driversQuery.Where(u => u.UserWarehouses.Any(uw => uw.WarehouseId == warehouseId && uw.IsActive));
            }

            var driverIdsWithoutRate = await driversQuery
                .Where(u => !_db.DriverRates.Any(dr =>
                    dr.DriverId == u.Id &&
                    dr.WarehouseId == warehouseId))
                .Select(u => (long)u.Id)
                .ToListAsync(ct);

            if (driverIdsWithoutRate.Count == 0)
                return new List<long>();

            var now = DateTime.UtcNow;

            var newRates = driverIdsWithoutRate.Select(id => new DriverRate
            {
                DriverId = id,
                WarehouseId = warehouseId,

                RateType = "PerStop",
                BaseAmount = baseAmount.Value,

                EffectiveFrom = effectiveDate,
                EffectiveTo = null,
                UpdatedAt = now,

                ExtraAmount = 0,
                DailyAmount = 0,
                MinPayPerRoute = null,
                OverStopBonusThreshold = null,
                OverStopBonusPerStop = null,
                FailedStopPenalty = null,
                RescueStopRate = null,
                NightDeliveryBonus = null
            }).ToList();

            await _db.DriverRates.AddRangeAsync(newRates, ct);
            await _db.SaveChangesAsync(ct);

            return driverIdsWithoutRate;
        }

        private long GetUserId()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return long.Parse(id!);
        }

        [HttpGet("my-paid-summary")]
        public async Task<IActionResult> GetMyPaidSummary()
        {
            var driverId = GetUserId();

            var paidRuns = await _db.PayRuns
                .Include(x => x.PayPeriod)
                .Where(x =>
                    x.DriverId == driverId &&
                    x.Status == "Approved" &&
                    x.ApprovedAt != null)
                .OrderByDescending(x => x.ApprovedAt)
                .ToListAsync();

            var lastPayment = paidRuns.FirstOrDefault();

            var response = new
            {
                totalCollected = paidRuns.Sum(x => x.NetAmount),
                totalGross = paidRuns.Sum(x => x.GrossAmount),
                totalDeductions = paidRuns.Sum(x => x.Adjustments),
                paidPeriods = paidRuns.Count,

                lastPayment = lastPayment == null ? null : new
                {
                    payRunId = lastPayment.Id,
                    period = $"{lastPayment.PayPeriod.StartDate:MMM dd} - {lastPayment.PayPeriod.EndDate:MMM dd}",
                    startDate = lastPayment.PayPeriod.StartDate,
                    endDate = lastPayment.PayPeriod.EndDate,
                    grossAmount = lastPayment.GrossAmount,
                    adjustments = lastPayment.Adjustments,
                    netAmount = lastPayment.NetAmount,
                    paidDate = lastPayment.ApprovedAt,
                    status = lastPayment.Status
                }
            };

            return Ok(response);
        }

        // GET: api/DriverPayroll/my-paid-history
        [Authorize]
        [HttpGet("my-paid-history")]
        public async Task<IActionResult> GetMyPaidHistory()
        {
            var driverId = GetUserId();

            var history = await _db.PayRuns
                .Include(x => x.PayPeriod)
                .Include(x => x.Lines)
                .Where(x =>
                    x.DriverId == driverId &&
                    x.Status == "Approved" &&
                    x.ApprovedAt != null)
                .OrderByDescending(x => x.PayPeriod.StartDate)
                .Select(x => new
                {
                    payRunId = x.Id,
                    payPeriodId = x.PayPeriodId,
                    startDate = x.PayPeriod.StartDate,
                    endDate = x.PayPeriod.EndDate,
                    paidDate = x.ApprovedAt,
                    grossAmount = x.GrossAmount,
                    adjustments = x.Adjustments,
                    netAmount = x.NetAmount,
                    status = x.Status,

                    routes = x.Lines
                        .Where(l => l.SourceType == "Route")
                        .Select(l => l.SourceId)
                        .Distinct()
                        .Count(),

                    stops = x.Lines
                        .Where(l => l.SourceType == "Stop" || l.SourceType == "Route")
                        .Sum(l => l.Qty)
                })
                .ToListAsync();

            return Ok(history);
        }

        // GET: api/DriverPayroll/my-paid-detail/5
        [Authorize]
        [HttpGet("my-paid-detail/{payRunId:long}")]
        public async Task<IActionResult> GetMyPaidDetail(long payRunId)
        {
            var driverId = GetUserId();

            var payRun = await _db.PayRuns
                .Include(x => x.PayPeriod)
                .Include(x => x.Lines)
                .Include(x => x.AdjustmentsList)
                .FirstOrDefaultAsync(x =>
                    x.Id == payRunId &&
                    x.DriverId == driverId &&
                    x.Status == "Approved" &&
                    x.ApprovedAt != null);

            if (payRun == null)
                return NotFound(new { message = "Paid payroll not found." });

            var response = new
            {
                payRunId = payRun.Id,
                payPeriodId = payRun.PayPeriodId,
                startDate = payRun.PayPeriod.StartDate,
                endDate = payRun.PayPeriod.EndDate,
                paidDate = payRun.ApprovedAt,
                grossAmount = payRun.GrossAmount,
                adjustments = payRun.Adjustments,
                netAmount = payRun.NetAmount,
                status = payRun.Status,

                lines = payRun.Lines
                    .OrderBy(x => x.RouteDate)
                    .Select(x => new
                    {
                        x.Id,
                        x.SourceType,
                        x.SourceId,
                        x.Description,
                        x.Qty,
                        x.Rate,
                        x.Amount,
                        x.RouteDate,
                        x.ZoneId,
                        x.ZoneArea,
                        x.Tags
                    }),

                adjustmentsList = payRun.AdjustmentsList.Select(a => new
                {
                    a.Id,
                    a.Amount,
                    a.Type,
                    a.CreatedAt
                })
            };

            return Ok(response);
        }

        // GET: api/DriverPayroll/my-paid-monthly?year=2026&month=6
        [Authorize(Roles = "Driver")]
        [HttpGet("my-paid-monthly")]
        public async Task<IActionResult> GetMyPaidMonthly([FromQuery] int year, [FromQuery] int month)
        {
            var driverId = GetUserId();

            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1);

            var payrolls = await _db.PayRuns
                .Include(x => x.PayPeriod)
                .Where(x =>
                    x.DriverId == driverId &&
                    x.Status == "Approved" &&
                    x.ApprovedAt != null &&
                    x.ApprovedAt >= start &&
                    x.ApprovedAt < end)
                .OrderBy(x => x.ApprovedAt)
                .Select(x => new
                {
                    x.Id,
                    x.GrossAmount,
                    x.Adjustments,
                    x.NetAmount,
                    x.ApprovedAt,
                    x.PayPeriod.StartDate,
                    x.PayPeriod.EndDate
                })
                .ToListAsync();

            return Ok(new
            {
                year,
                month,
                totalCollected = payrolls.Sum(x => x.NetAmount),
                totalGross = payrolls.Sum(x => x.GrossAmount),
                totalDeductions = payrolls.Sum(x => x.Adjustments),
                payments = payrolls
            });
        }

        [HttpGet("periods/{id:long}/insights")]
public async Task<ActionResult<PayrollInsightsDto>> GetPayrollInsights(long id)
{
    var period = await _db.PayPeriods
        .AsNoTracking()
        .FirstOrDefaultAsync(p => p.Id == id);

    if (period is null)
        return NotFound("PayPeriod does not exist.");

    var start = period.StartDate;
    var end = period.EndDate;
    var endExclusive = end.AddDays(1);

    var currentStartDt = start.ToDateTime(TimeOnly.MinValue);
    var currentEndDt = endExclusive.ToDateTime(TimeOnly.MinValue);

    var days = end.DayNumber - start.DayNumber + 1;

    var prevStart = start.AddDays(-days);
    var prevEnd = start.AddDays(-1);
    var prevEndExclusive = prevEnd.AddDays(1);

    var prevStartDt = prevStart.ToDateTime(TimeOnly.MinValue);
    var prevEndDt = prevEndExclusive.ToDateTime(TimeOnly.MinValue);

    var warehouseId = period.WarehouseId;

    var currentDrivers = await _db.Routes
        .AsNoTracking()
        .Where(r =>
            r.UserId != null &&
            r.routeStatus == RouteStatus.Completed &&
            r.DeliveryStops > 0 &&
            r.Date >= currentStartDt &&
            r.Date < currentEndDt &&
            (!warehouseId.HasValue || r.WarehouseId == warehouseId.Value))
        .Select(r => r.UserId!.Value)
        .Distinct()
        .ToListAsync();

    var previousDrivers = await _db.Routes
        .AsNoTracking()
        .Where(r =>
            r.UserId != null &&
            r.routeStatus == RouteStatus.Completed &&
            r.DeliveryStops > 0 &&
            r.Date >= prevStartDt &&
            r.Date < prevEndDt &&
            (!warehouseId.HasValue || r.WarehouseId == warehouseId.Value))
        .Select(r => r.UserId!.Value)
        .Distinct()
        .ToListAsync();

    var currentSet = currentDrivers.ToHashSet();
    var previousSet = previousDrivers.ToHashSet();

    var retainedDrivers = previousSet.Intersect(currentSet).Count();
    var newDrivers = currentSet.Except(previousSet).Count();
    var lostDrivers = previousSet.Except(currentSet).Count();

    var retentionRate = previousSet.Count > 0
        ? Math.Round((decimal)retainedDrivers * 100m / previousSet.Count, 2)
        : 0m;

    var churnRate = previousSet.Count > 0
        ? Math.Round((decimal)lostDrivers * 100m / previousSet.Count, 2)
        : 0m;

    var runs = await _db.PayRuns
        .AsNoTracking()
        .Where(r => r.PayPeriodId == period.Id)
        .Select(r => new
        {
            r.DriverId,
            r.NetAmount
        })
        .ToListAsync();

    var totalNet = runs.Sum(r => r.NetAmount);

    var averagePay = runs.Count > 0
        ? Math.Round(totalNet / runs.Count, 2)
        : 0m;

    var riskDrivers = new List<DriverStoppedWorkingDto>();

    try
    {
        var warehouseIds = await _db.Routes
            .AsNoTracking()
            .Where(r =>
                r.WarehouseId.HasValue &&
                r.UserId != null &&
                r.Date >= currentStartDt &&
                r.Date < currentEndDt &&
                (!warehouseId.HasValue || r.WarehouseId == warehouseId.Value))
            .Select(r => r.WarehouseId!.Value)
            .Distinct()
            .ToListAsync();

        if (warehouseIds.Any())
        {
            var warehouseLastRouteDate = await _db.Routes
                .AsNoTracking()
                .Where(r =>
                    r.WarehouseId.HasValue &&
                    warehouseIds.Contains(r.WarehouseId.Value))
                .MaxAsync(r => (DateTime?)r.Date);

            if (warehouseLastRouteDate.HasValue)
            {
                var driversInPeriod = await _db.Users
                    .AsNoTracking()
                    .Where(u =>
                        u.IsActive &&
                        u.UserRole == global::User.Role.Driver &&
                        currentDrivers.Contains(u.Id))
                    .Select(u => new
                    {
                        u.Id,
                        u.Name,
                        u.LastName
                    })
                    .ToListAsync();

                var driverIds = driversInPeriod
                    .Select(d => d.Id)
                    .ToList();

                var lastRouteMap = (await _db.Routes
                    .AsNoTracking()
                    .Where(r =>
                        r.UserId != null &&
                        driverIds.Contains(r.UserId.Value))
                    .GroupBy(r => r.UserId!.Value)
                    .Select(g => new
                    {
                        DriverId = g.Key,
                        LastDate = g.Max(r => r.Date)
                    })
                    .ToListAsync())
                    .ToDictionary(x => x.DriverId, x => x.LastDate);

                var warehouseMax = warehouseLastRouteDate.Value.Date;

                riskDrivers = driversInPeriod
                    .Where(d =>
                        lastRouteMap.TryGetValue(d.Id, out var last) &&
                        last.Date < warehouseMax)
                    .Select(d =>
                    {
                        var last = lastRouteMap[d.Id];

                        return new DriverStoppedWorkingDto
                        {
                            DriverId = d.Id,
                            DriverName = $"{d.Name} {d.LastName}".Trim(),
                            LastRouteDate = last.ToString("yyyy-MM-dd"),
                            DaysSinceLastRoute = (warehouseMax - last.Date).Days
                        };
                    })
                    .OrderByDescending(x => x.DaysSinceLastRoute)
                    .ToList();
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error computing payroll insights risk drivers.");
    }

    decimal averageDriverLifetimeWeeks = 0m;
    var seniority = new DriverSeniorityDto();

    try
    {
        var warehouseLastRouteDate = await _db.Routes
            .AsNoTracking()
            .Where(r =>
                r.WarehouseId.HasValue &&
                (!warehouseId.HasValue || r.WarehouseId == warehouseId.Value))
            .MaxAsync(r => (DateTime?)r.Date);

        if (warehouseLastRouteDate.HasValue && currentDrivers.Any())
        {
            var lifetimes = await _db.Routes
                .AsNoTracking()
                .Where(r =>
                    r.UserId != null &&
                    currentDrivers.Contains(r.UserId.Value) &&
                    r.routeStatus == RouteStatus.Completed &&
                    r.DeliveryStops > 0 &&
                    (!warehouseId.HasValue || r.WarehouseId == warehouseId.Value))
                .GroupBy(r => r.UserId!.Value)
                .Select(g => new
                {
                    DriverId = g.Key,
                    FirstRoute = g.Min(x => x.Date),
                    LastRoute = g.Max(x => x.Date)
                })
                .ToListAsync();

            var weeks = lifetimes
                .Select(x =>
                    (warehouseLastRouteDate.Value.Date - x.FirstRoute.Date).TotalDays / 7.0)
                .Where(x => x >= 0)
                .ToList();

            if (weeks.Any())
            {
                averageDriverLifetimeWeeks = Math.Round((decimal)weeks.Average(), 2);

                seniority = new DriverSeniorityDto
                {
                    Weeks0To2 = weeks.Count(x => x < 2),
                    Weeks3To8 = weeks.Count(x => x >= 2 && x < 8),
                    Months2To6 = weeks.Count(x => x >= 8 && x < 24),
                    Months6Plus = weeks.Count(x => x >= 24)
                };
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error computing driver lifetime insights.");
    }

    var dto = new PayrollInsightsDto
    {
        PayPeriodId = period.Id,
        StartDate = start.ToString("yyyy-MM-dd"),
        EndDate = end.ToString("yyyy-MM-dd"),

        ActiveDrivers = currentSet.Count,
        PreviousActiveDrivers = previousSet.Count,
        NewDrivers = newDrivers,
        LostDrivers = lostDrivers,
        RetainedDrivers = retainedDrivers,

        RetentionRate = retentionRate,
        ChurnRate = churnRate,

        TotalNet = totalNet,
        AveragePay = averagePay,

        DriversAtRisk = riskDrivers.Count,
        RiskDrivers = riskDrivers,

        AverageDriverLifetimeWeeks = averageDriverLifetimeWeeks,
        Seniority = seniority
    };

    return Ok(dto);
}






    public sealed class CreateAdjustmentRequest
    {
        public long PayRunId { get; set; }
        public string Type { get; set; } = "Manual";
        public string Reason { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    public class DriverRateDto
        {
            public long Id { get; set; }
            public long DriverId { get; set; }
            public string? DriverName { get; set; }
            public string? DriverLastName { get; set; }
            public string RateType { get; set; } = null!;
            public decimal BaseAmount { get; set; }
            public decimal? MinPayPerRoute { get; set; }
            public int? OverStopBonusThreshold { get; set; }
            public decimal? OverStopBonusPerStop { get; set; }
            public decimal? FailedStopPenalty { get; set; }
            public decimal? RescueStopRate { get; set; }
            public decimal? NightDeliveryBonus { get; set; }
            public DateOnly EffectiveFrom { get; set; }
            public DateOnly? EffectiveTo { get; set; }
            public string? DriverFullName =>
                string.Join(" ", new[] { DriverName, DriverLastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

            public int? WarehouseId { get; set; }
            public decimal? DailyAmount { get; set; }
            public decimal? ExtraAmount { get; set; }
        }

        public sealed class UpdateDriverRateRequest
        {
            public long Id { get; set; }
            public long DriverId { get; set; }
            public string RateType { get; set; } = default!; // PerRoute | PerStop | PerPackage | PerMile | Hourly | Mixed

            public decimal? BaseAmount { get; set; }
            public decimal? MinPayPerRoute { get; set; }
            public int? OverStopBonusThreshold { get; set; }
            public decimal? OverStopBonusPerStop { get; set; }
            public decimal? FailedStopPenalty { get; set; }
            public decimal? RescueStopRate { get; set; }
            public decimal? NightDeliveryBonus { get; set; }

            public DateOnly? EffectiveFrom { get; set; }   // opcional en update
            public DateOnly? EffectiveTo { get; set; }     // opcional en update
            public decimal? DailyAmount { get; set; }
            public decimal? ExtraAmount { get; set; }
        }
        public sealed class PayrollInsightsDto
{
    public long PayPeriodId { get; set; }
    public string StartDate { get; set; } = null!;
    public string EndDate { get; set; } = null!;

    public int ActiveDrivers { get; set; }
    public int PreviousActiveDrivers { get; set; }
    public int NewDrivers { get; set; }
    public int LostDrivers { get; set; }
    public int RetainedDrivers { get; set; }

    public decimal RetentionRate { get; set; }
    public decimal ChurnRate { get; set; }

    public decimal TotalNet { get; set; }
    public decimal AveragePay { get; set; }

    public int DriversAtRisk { get; set; }
    public List<DriverStoppedWorkingDto> RiskDrivers { get; set; } = new();
    public decimal AverageDriverLifetimeWeeks { get; set; }
    public DriverSeniorityDto Seniority { get; set; } = new();
}
public sealed class DriverSeniorityDto
{
    public int Weeks0To2 { get; set; }
    public int Weeks3To8 { get; set; }
    public int Months2To6 { get; set; }
    public int Months6Plus { get; set; }
}
    }
}
