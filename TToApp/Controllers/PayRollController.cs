using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using TToApp.Model;
using TToApp.Services.Payroll;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PayRollController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PayrollService _service;
        private readonly ILogger<PayRollController> _logger;
        public PayRollController(ApplicationDbContext db, PayrollService service, ILogger<PayRollController> logger)
        {
            _db = db;
            _service = service;
            _logger = logger;
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
        }

        public sealed class WarehouseNullZoneSummaryDto
        {
            public int WarehouseId { get; set; }
            public Dictionary<string, int> NullZoneRoutesByDate { get; set; } = new();
        }

        public sealed class PeriodSummaryDto
        {
            public long PayPeriodId { get; set; }
            public string StartDate { get; set; } = null!;
            public string EndDate { get; set; } = null!;
            public List<PeriodSummaryRow> Drivers { get; set; } = new();
            public List<WarehouseNullZoneSummaryDto> OnTracNullZoneRoutes { get; set; } = new();
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

            // ✅ Determinar si el warehouse es OnTrac (solo si viene WarehouseId)
            bool isOnTrac = false;
            int? widInt = null;

            // if (req.WarehouseId.HasValue)
            // {
            //     widInt = (int)req.WarehouseId.Value;

            //     isOnTrac = await _db.Warehouses
            //         .AsNoTracking()
            //         .AnyAsync(w =>
            //             w.Id == widInt.Value &&
            //             w.CompanyId == req.CompanyId &&
            //             (w.Company ?? "").Trim().ToLower() == "ontrac"
            //         );
            // }

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

            // Get distinct warehouseIds
            var warehouseIds = await routesQ
                .Select(x => x.r.WarehouseId)
                .Where(id => id != null)
                .Distinct()
                .ToListAsync();
            
            // Clacify is OnTrac per warehouse
            var onTracWarehouseIds = await _db.Warehouses
                .AsNoTracking()
                .Where(w =>
                    warehouseIds.Contains(w.Id) &&
                    w.CompanyId == req.CompanyId &&
                    (w.Company ?? "").Trim().ToLower() == "ontrac"
                )
                .Select(w => w.Id)
                .ToListAsync();
            // var nonOnTracWarehouseIds = warehouseIds
            //     .Except(onTracWarehouseIds)
            //     .ToList();

            //
           
            var onTracWarehousesWithNullZone = await routesQ
                .Where(x =>
                    x.z == null &&
                    x.r.WarehouseId.HasValue &&
                    onTracWarehouseIds.Contains(x.r.WarehouseId.Value)
                )
                .Select(x => x.r.WarehouseId!.Value)
                .Distinct()
                .ToListAsync();

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

                var onTracNullZoneByWarehouse = flat
                    .GroupBy(x => x.WarehouseId)
                    .Select(g => new
                    {
                        WarehouseId = g.Key,
                        NullZoneRoutesByDate = g.ToDictionary(
                            x => x.Date.ToString("yyyy-MM-dd"),
                            x => x.Count
                        )
                    })
                    .ToList();

                // return Ok(onTracNullZoneByWarehouse);

            routesQ = routesQ.Where(x =>
                x.r.WarehouseId.HasValue
                && !onTracWarehousesWithNullZone.Contains(x.r.WarehouseId.Value)
                );

            var driverIds = await routesQ
                .Select(x => (long)x.r.UserId!)
                .Distinct()
                .ToListAsync();

            // 3) Evitar recalcular si ya existe (a menos que se pida)
            HashSet<long> already = new();
            if (!req.RecalculateAll)
            {
                already = (await _db.PayRuns
                    .Where(x => x.PayPeriodId == period.Id)
                    .Select(x => x.DriverId)
                    .ToListAsync()).ToHashSet();
            }

            // 4) Calcular por driver
            foreach (var driverId in driverIds)
            {
                if (!req.RecalculateAll && already.Contains(driverId)) continue;

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

            // 5) Summary
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
                OnTracNullZoneRoutes = onTracNullZoneByWarehouse
                    .Select(x => new WarehouseNullZoneSummaryDto
                    {
                        WarehouseId = x.WarehouseId,
                        NullZoneRoutesByDate = x.NullZoneRoutesByDate
                    })
                    .ToList()

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
            if (period is null) return NotFound("PayPeriod no existe.");
            if (!string.Equals(period.Status, "Open", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Solo se puede bloquear un período en estado 'Open'.");

            period.Status = "Locked";
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Aprueba un PayRun (status: Draft -> Approved).</summary>
        [HttpPost("runs/{id:long}/approve")]
        public async Task<IActionResult> ApproveRun(long id)
        {
            var run = await _db.PayRuns.FindAsync(id);
            if (run is null) return NotFound("PayRun no existe.");
            run.Status = "Approved";
            await _db.SaveChangesAsync();
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

            if (run is null) return NotFound("PayRun no existe.");
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
                     Net = r.NetAmount
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
        [HttpGet("runs/{id:long}/export")]
        public async Task<IActionResult> ExportRun(long id, [FromQuery] string? filename = null)
        {
            var run = await _db.PayRuns
                .AsNoTracking()
                .Include(r => r.Lines)
                .Include(r => r.AdjustmentsList)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (run is null) return NotFound("PayRun no existe.");

            var sb = new StringBuilder();
            sb.AppendLine("Section,SourceType,SourceId,Description,Qty,Rate,Amount");

            // Líneas
            foreach (var l in run.Lines.OrderBy(l => l.SourceType).ThenBy(l => l.Id))
            {
                sb.Append("Lines,")
                  .Append(Escape(l.SourceType)).Append(',')
                  .Append(Escape(l.SourceId)).Append(',')
                  .Append(Escape(l.Description)).Append(',')
                  .Append(l.Qty.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(l.Rate.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(l.Amount.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }

            // Ajustes
            foreach (var a in run.AdjustmentsList.OrderBy(a => a.Id))
            {
                sb.Append("Adjustments,")
                  .Append(Escape(a.Type)).Append(',')
                  .Append(Escape(run.Id.ToString())).Append(',')
                  .Append(Escape(a.Reason)).Append(',')
                  .Append("1,")
                  .Append(a.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(a.Amount.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }

            // Totales
            sb.AppendLine();
            sb.AppendLine($"Totals,,DriverId,{run.DriverId},Gross,{run.GrossAmount.ToString(CultureInfo.InvariantCulture)},Net,{run.NetAmount.ToString(CultureInfo.InvariantCulture)}");

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

        // -------------------------
        // NUEVO: Materializar período (crea/actualiza PayRuns) y devolver summary
        // -------------------------
        /// <summary>
        /// Crea/obtiene el PayPeriod y calcula PayRuns de todos los drivers con rutas COMPLETED en ese rango.
        /// Filtra por ZoneId si se envía; si tu Zone tiene WarehouseId, filtra por almacén.
        /// </summary>
        /// 

        [HttpGet("driverRates")]
        public async Task<ActionResult<List<DriverRateDto>>> GetDriverRates(
    [FromQuery] long? driverId = null,
    [FromQuery] string? rateType = null,
    [FromQuery] bool onlyActive = false,
    [FromQuery] DateOnly? from = null,
    [FromQuery] DateOnly? to = null)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

            var q = _db.Set<DriverRate>().AsNoTracking().AsQueryable();

            if (driverId is not null)
                q = q.Where(r => r.DriverId == driverId);

            if (!string.IsNullOrWhiteSpace(rateType))
                q = q.Where(r => r.RateType == rateType);

            if (onlyActive)
                q = q.Where(r => r.EffectiveFrom <= today &&
                                 (r.EffectiveTo == null || r.EffectiveTo >= today));

            if (from is not null || to is not null)
            {
                var start = from ?? DateOnly.MinValue;
                var end = to ?? DateOnly.MaxValue;
                q = q.Where(r => r.EffectiveFrom <= end &&
                                 (r.EffectiveTo == null || r.EffectiveTo >= start));
            }

            var items = await (
                from r in q
                join u in _db.Set<User>() on r.DriverId equals u.Id into gj
                from u in gj.DefaultIfEmpty()
                orderby r.EffectiveFrom descending, r.Id descending
                select new DriverRateDto
                {
                    Id = r.Id,
                    DriverId = r.DriverId,                // ← incluye DriverId
                    DriverName = u != null ? u.Name : null,
                    DriverLastName = u != null ? u.LastName : null,
                    WarehouseId = u.WarehouseId,
                    RateType = r.RateType,
                    BaseAmount = r.BaseAmount,
                    MinPayPerRoute = r.MinPayPerRoute,
                    OverStopBonusThreshold = r.OverStopBonusThreshold,
                    OverStopBonusPerStop = r.OverStopBonusPerStop,
                    FailedStopPenalty = r.FailedStopPenalty,
                    RescueStopRate = r.RescueStopRate,
                    NightDeliveryBonus = r.NightDeliveryBonus,
                    EffectiveFrom = r.EffectiveFrom,
                    EffectiveTo = r.EffectiveTo
                }
            ).ToListAsync();

            return Ok(items);
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
                        EffectiveTo = r.EffectiveTo
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
            if (warehouseId <= 0)
                return BadRequest(new { message = "warehouseId inválido." });

            // 1) Traer warehouse y su rate default
            var warehouse = await _db.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == warehouseId, ct);

            if (warehouse is null)
                return NotFound(new { message = $"Warehouse {warehouseId} no existe." });

            // Ajusta el nombre según tu modelo:
            var baseAmount = warehouse.DriveRate; // <-- CAMBIA si tu propiedad se llama diferente

            if (baseAmount <= 0)
                return BadRequest(new { message = "El warehouse no tiene DriveRate válido (> 0)." });

            var today = new DateOnly(2025, 1, 1);

            // 2) Obtener drivers del warehouse que NO tienen rate
            //    (RoleId=3 según tu mensaje)
            var driverIdsWithoutRate = await _db.Users
                .AsNoTracking()
                .Where(u => u.UserRole == global::User.Role.Driver && u.WarehouseId == warehouseId)
                .Where(u => !_db.DriverRates.Any(dr => dr.DriverId == u.Id))
                .Select(u => (long)u.Id)
                .ToListAsync(ct);

            if (driverIdsWithoutRate.Count == 0)
            {
                return Ok(new
                {
                    created = 0,
                    message = "No hay drivers sin DriverRate en ese warehouse."
                });
            }

            // 3) Crear DriverRates (bulk)
            var now = DateTime.UtcNow;

            var newRates = driverIdsWithoutRate.Select(driverId => new DriverRate
            {
                DriverId = driverId,
                RateType = "PerStop",          // o "PerStop" si ese es tu default
                BaseAmount = (decimal)baseAmount,        // desde el warehouse
                EffectiveFrom = today,
                EffectiveTo = null,
                UpdatedAt = now,

                // opcional: defaults
                MinPayPerRoute = null,
                OverStopBonusThreshold = null,
                OverStopBonusPerStop = null,
                FailedStopPenalty = null,
                RescueStopRate = null,
                NightDeliveryBonus = null
            }).ToList();

            await _db.DriverRates.AddRangeAsync(newRates, ct);
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                created = newRates.Count,
                warehouseId,
                baseAmount,
                rateType = "PerStop",
                driverIds = driverIdsWithoutRate
            });
        }

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
        }
    }

