using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/dashboard")]
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public DashboardController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet("driver/{driverId:int}/last-route")]
        public async Task<IActionResult> GetDriverLastRoute(int driverId, CancellationToken ct)
        {
            // Última ruta completada del driver
            var route = await _db.Routes
                .AsNoTracking()
                .Where(r => r.UserId == driverId && r.routeStatus == RouteStatus.Completed)
                .Include(r => r.Zone)
                .Include(r => r.Warehouse)
                .OrderByDescending(r => r.Date)
                .FirstOrDefaultAsync(ct);

            if (route is null)
                return NotFound(new { message = $"No completed routes found for driver {driverId}." });

            // Paquetes de esa ruta
            var packages = await _db.Packages
                .AsNoTracking()
                .Where(p => p.RoutesId == route.Id)
                .Select(p => new
                {
                    p.Id,
                    p.Tracking,
                    p.Address,
                    p.City,
                    p.State,
                    p.ZipCode,
                    p.Distance,
                    p.ScanLat,
                    p.ScanLon,
                    p.AddrLat,
                    p.AddrLon,
                    p.IncidentDate,
                    Status       = p.Status.ToString(),
                    p.DaysElapsed,
                    p.Notified,
                    p.RSP,
                    p.Brand,
                    ReviewStatus = p.ReviewStatus.ToString(),
                    p.Weight
                })
                .ToListAsync(ct);

            // Punch del mismo día que la ruta
            var routeDate = route.Date.Date;
            var punches = await _db.DriverPunches
                .AsNoTracking()
                .Where(p =>
                    p.DriverId == driverId &&
                    p.OccurredAtUtc.Date == routeDate)
                .OrderBy(p => p.OccurredAtUtc)
                .ToListAsync(ct);

            var arrival   = punches.FirstOrDefault(p => p.PunchType == PunchType.Arrival);
            var departure = punches.LastOrDefault(p => p.PunchType == PunchType.Departure);

            return Ok(new
            {
                Route = new
                {
                    route.Id,
                    route.RouteCode,
                    Date           = route.Date.ToString("yyyy-MM-dd"),
                    route.DeliveryStops,
                    Delivered = Math.Max(0, route.Volumen - route.Attempts), // Asumiendo que cada intento fallido reduce el volumen entregado
                    route.Volumen,
                    route.Attempts,
                    route.CNL,
                    Zone = route.Zone == null ? null : new
                    {
                        route.Zone.Id,
                        route.Zone.ZoneCode
                    },
                    Warehouse = route.Warehouse == null ? null : new
                    {
                        route.Warehouse.Id,
                        route.Warehouse.Name
                    }
                },
                Packages = packages,
                Punch = new
                {
                    Arrival   = arrival?.OccurredAtUtc,
                    Departure = departure?.OccurredAtUtc
                }
            });
        }

        [HttpGet("driver/{driverId:int}/routes")]
        public async Task<IActionResult> GetDriverRoutesAdvanced(
            int driverId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            // 1) Rutas del driver — filtro por rango si se provee, sino últimas 10
            var query = _db.Routes
                .AsNoTracking()
                .Where(r => r.UserId == driverId)
                .Include(r => r.Zone)
                .Include(r => r.Warehouse)
                .OrderByDescending(r => r.Date)
                .AsQueryable();

            if (from.HasValue || to.HasValue)
            {
                var fromDt = from?.Date ?? DateTime.MinValue;
                var toDt   = (to?.Date ?? DateTime.UtcNow.Date).AddDays(1);
                query = query.Where(r => r.Date >= fromDt && r.Date < toDt);
            }
            else
            {
                query = query.Take(10);
            }

            var routes = await query.ToListAsync(ct);

            if (routes.Count == 0)
                return Ok(new { Routes = Array.Empty<object>(), TotalCharged = 0m });

            // 2) PayRunLines relacionadas a esas rutas (SourceId = routeId)
            var routeIdStrings = routes.Select(r => r.Id.ToString()).ToHashSet();

            var lines = await _db.PayRunLines
                .AsNoTracking()
                .Where(l => l.SourceId != null && routeIdStrings.Contains(l.SourceId))
                .Select(l => new { l.SourceId, l.Amount, l.Tags })
                .ToListAsync(ct);

            // Agrupar montos de PayRunLines por routeId
            var amountByRoute = lines
                .GroupBy(l => l.SourceId!)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

            // 2b) PayrollAdjustments del driver en el período cubierto por las rutas
            var minDate = DateOnly.FromDateTime(routes.Min(r => r.Date));
            var maxDate = DateOnly.FromDateTime(routes.Max(r => r.Date));

            var totalAdjustments = await _db.PayRuns
                .AsNoTracking()
                .Where(pr =>
                    pr.DriverId == driverId &&
                    pr.PayPeriod != null &&
                    pr.PayPeriod.StartDate <= maxDate &&
                    pr.PayPeriod.EndDate >= minDate)
                .SumAsync(pr => (decimal?)pr.Adjustments) ?? 0m;

            // 3) Construir respuesta
            var result = routes.Select(r =>
            {
                var charged = amountByRoute.TryGetValue(r.Id.ToString(), out var amt) ? amt : (decimal?)null;
                return new
                {
                    r.Id,
                    r.RouteCode,
                    Date          = r.Date.ToString("yyyy-MM-dd"),
                    r.DeliveryStops,
                    r.Volumen,
                    Delivered     = Math.Max(0, r.Volumen - r.Attempts),
                    r.CNL,
                    r.Attempts,
                    Status        = r.routeStatus.ToString(),
                    Zone          = r.Zone == null ? null : new { r.Zone.Id, r.Zone.ZoneCode },
                    Warehouse     = r.Warehouse == null ? null : new { r.Warehouse.Id, r.Warehouse.Name },
                    ChargedAmount = charged
                };
            }).ToList();

            return Ok(new
            {
                Routes = result,
                Totals = new
                {
                    RouteCount    = result.Count,
                    DeliveryStops = routes.Sum(r => r.DeliveryStops),
                    Volumen       = routes.Sum(r => r.Volumen),
                    Delivered     = routes.Sum(r => Math.Max(0, r.Volumen - r.Attempts)),
                    CNL           = routes.Sum(r => r.CNL),
                    Attempts      = routes.Sum(r => r.Attempts),
                    TotalEarned       = amountByRoute.Values.Sum(),
                    TotalAdjustments  = totalAdjustments,
                    TotalCharged      = amountByRoute.Values.Sum() + totalAdjustments
                }
            });
        }
    }
}
