using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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

        [HttpGet("driver/me/last-route")]
        public async Task<IActionResult> GetMyLastRoute(CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var driverId))
                return Unauthorized(new { message = "Invalid token. DriverId not found." });

            return await GetDriverLastRouteInternal(driverId, ct);
        }

        [HttpGet("driver/{driverId:int}/last-route")]
        public async Task<IActionResult> GetDriverLastRoute(int driverId, CancellationToken ct)
        {
            return await GetDriverLastRouteInternal(driverId, ct);
        }

        private async Task<IActionResult> GetDriverLastRouteInternal(int driverId, CancellationToken ct)
        {
            var route = await _db.Routes
                .AsNoTracking()
                .Where(r => r.UserId == driverId)
                .Include(r => r.Zone)
                .Include(r => r.Warehouse)
                .OrderByDescending(r => r.routeStatus == RouteStatus.Completed)
                .ThenByDescending(r => r.Date)
                .ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);

            if (route == null)
            {
                return Ok(new
                {
                    message = $"No routes found for driver {driverId}.",
                    route = (object?)null,
                    packages = Array.Empty<object>(),
                    punch = new
                    {
                        arrival = (DateTime?)null,
                        departure = (DateTime?)null
                    }
                });
            }

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
                    status = p.Status.ToString(),
                    p.DaysElapsed,
                    p.Notified,
                    p.RSP,
                    p.Brand,
                    reviewStatus = p.ReviewStatus.ToString(),
                    p.Weight
                })
                .ToListAsync(ct);

            var routeDate = route.Date.Date;

            var punches = await _db.DriverPunches
                .AsNoTracking()
                .Where(p =>
                    p.DriverId == driverId &&
                    p.OccurredAtUtc.Date == routeDate)
                .OrderBy(p => p.OccurredAtUtc)
                .ToListAsync(ct);

            var arrival = punches.FirstOrDefault(p => p.PunchType == PunchType.Arrival);
            var departure = punches.LastOrDefault(p => p.PunchType == PunchType.Departure);

            var volumen = route.Volumen;
            var attempts = route.Attempts;
            var cnl = route.CNL;

            var los = volumen > 0
                ? Math.Round(((decimal)(volumen - (attempts + cnl)) / volumen) * 100, 2)
                : 0;

            return Ok(new
            {
                route = new
                {
                    route.Id,
                    route.RouteCode,
                    date = route.Date.ToString("yyyy-MM-dd"),
                    route.DeliveryStops,
                    route.Volumen,
                    delivered = Math.Max(0, route.Volumen - route.Attempts),
                    route.Attempts,
                    route.CNL,
                    los,
                    status = route.routeStatus.ToString(),
                    zone = route.Zone == null ? null : new
                    {
                        route.Zone.Id,
                        route.Zone.ZoneCode
                    },
                    warehouse = route.Warehouse == null ? null : new
                    {
                        route.Warehouse.Id,
                        route.Warehouse.Name,
                        route.Warehouse.City,
                        route.Warehouse.State
                    }
                },
                packages,
                punch = new
                {
                    arrival = arrival?.OccurredAtUtc,
                    departure = departure?.OccurredAtUtc
                }
            });
        }

        [HttpGet("driver/me/routes")]
        public async Task<IActionResult> GetMyRoutes(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var driverId))
                return Unauthorized(new { message = "Invalid token. DriverId not found." });

            return await GetDriverRoutesInternal(driverId, from, to, ct);
        }

        [HttpGet("driver/{driverId:int}/routes")]
        public async Task<IActionResult> GetDriverRoutesAdvanced(
            int driverId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            return await GetDriverRoutesInternal(driverId, from, to, ct);
        }

        private async Task<IActionResult> GetDriverRoutesInternal(
            int driverId,
            DateTime? from,
            DateTime? to,
            CancellationToken ct)
        {
            var query = _db.Routes
                .AsNoTracking()
                .Where(r => r.UserId == driverId)
                .Include(r => r.Zone)
                .Include(r => r.Warehouse)
                .OrderByDescending(r => r.Date)
                .ThenByDescending(r => r.Id)
                .AsQueryable();

            if (from.HasValue || to.HasValue)
            {
                var fromDt = from?.Date ?? DateTime.MinValue;
                var toDt = (to?.Date ?? DateTime.UtcNow.Date).AddDays(1);

                query = query.Where(r => r.Date >= fromDt && r.Date < toDt);
            }
            else
            {
                query = query.Take(10);
            }

            var routes = await query.ToListAsync(ct);

            if (routes.Count == 0)
            {
                return Ok(new
                {
                    routes = Array.Empty<object>(),
                    totals = new
                    {
                        routeCount = 0,
                        deliveryStops = 0,
                        volumen = 0,
                        delivered = 0,
                        cnl = 0,
                        attempts = 0,
                        totalEarned = 0m,
                        totalAdjustments = 0m,
                        totalCharged = 0m
                    }
                });
            }

            var routeIdStrings = routes.Select(r => r.Id.ToString()).ToHashSet();

            var lines = await _db.PayRunLines
                .AsNoTracking()
                .Where(l => l.SourceId != null && routeIdStrings.Contains(l.SourceId))
                .Select(l => new { l.SourceId, l.Amount })
                .ToListAsync(ct);

            var amountByRoute = lines
                .GroupBy(l => l.SourceId!)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

            var minDate = DateOnly.FromDateTime(routes.Min(r => r.Date));
            var maxDate = DateOnly.FromDateTime(routes.Max(r => r.Date));

            var totalAdjustments = await _db.PayRuns
                .AsNoTracking()
                .Where(pr =>
                    pr.DriverId == driverId &&
                    pr.PayPeriod != null &&
                    pr.PayPeriod.StartDate <= maxDate &&
                    pr.PayPeriod.EndDate >= minDate)
                .SumAsync(pr => (decimal?)pr.Adjustments, ct) ?? 0m;

            var result = routes.Select(r =>
            {
                var charged = amountByRoute.TryGetValue(r.Id.ToString(), out var amt) ? amt : 0m;

                var los = r.Volumen > 0
                    ? Math.Round(((decimal)(r.Volumen - (r.Attempts + r.CNL)) / r.Volumen) * 100, 2)
                    : 0;

                return new
                {
                    r.Id,
                    r.RouteCode,
                    date = r.Date.ToString("yyyy-MM-dd"),
                    r.DeliveryStops,
                    r.Volumen,
                    delivered = Math.Max(0, r.Volumen - r.Attempts),
                    r.CNL,
                    r.Attempts,
                    los,
                    status = r.routeStatus.ToString(),
                    zone = r.Zone == null ? null : new { r.Zone.Id, r.Zone.ZoneCode },
                    warehouse = r.Warehouse == null ? null : new
                    {
                        r.Warehouse.Id,
                        r.Warehouse.Name,
                        r.Warehouse.City,
                        r.Warehouse.State
                    },
                    chargedAmount = charged
                };
            }).ToList();

            return Ok(new
            {
                routes = result,
                totals = new
                {
                    routeCount = result.Count,
                    deliveryStops = routes.Sum(r => r.DeliveryStops),
                    volumen = routes.Sum(r => r.Volumen),
                    delivered = routes.Sum(r => Math.Max(0, r.Volumen - r.Attempts)),
                    cnl = routes.Sum(r => r.CNL),
                    attempts = routes.Sum(r => r.Attempts),
                    totalEarned = amountByRoute.Values.Sum(),
                    totalAdjustments,
                    totalCharged = amountByRoute.Values.Sum() + totalAdjustments
                }
            });
        }
    }
}