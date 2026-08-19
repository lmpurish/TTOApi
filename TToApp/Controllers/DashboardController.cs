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

        // ─── Overview ───────────────────────────────────────────────────────────

        [HttpGet("driver/me/overview")]
        public async Task<IActionResult> GetMyOverview(CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var driverId))
                return Unauthorized(new { message = "Invalid token. DriverId not found." });
            return await GetDriverOverviewInternal(driverId, ct);
        }

        [HttpGet("driver/{driverId:int}/overview")]
        public async Task<IActionResult> GetDriverOverview(int driverId, CancellationToken ct)
            => await GetDriverOverviewInternal(driverId, ct);

        private async Task<IActionResult> GetDriverOverviewInternal(int driverId, CancellationToken ct)
        {
            var user = await _db.Users
                .AsNoTracking()
                .Include(u => u.Profile)
                .Include(u => u.Warehouse)
                .FirstOrDefaultAsync(u => u.Id == driverId, ct);

            if (user == null)
                return NotFound(new { message = $"Driver {driverId} not found." });

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            int? daysWithCompany = user.InitialDate.HasValue
                ? today.DayNumber - user.InitialDate.Value.DayNumber
                : null;

            // ── Routes aggregate ─────────────────────────────────────────────
            var routeStats = await _db.Routes
                .AsNoTracking()
                .Where(r => r.UserId == driverId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalRoutes  = g.Count(),
                    TotalVolumen = g.Sum(r => r.Volumen),
                    TotalDelivered = g.Sum(r => r.Volumen - r.Attempts),
                    TotalStops   = g.Sum(r => r.DeliveryStops),
                    LastRouteDate = g.Max(r => (DateTime?)r.Date)
                })
                .FirstOrDefaultAsync(ct);

            // ── Fines with package details ────────────────────────────────────
            var finesRaw = await _db.PayrollFines
                .AsNoTracking()
                .Include(f => f.Package)
                .Where(f => f.UserId == driverId)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync(ct);

            var finesList = finesRaw.Select(f => new
            {
                f.Id,
                f.Type,
                f.Amount,
                f.Description,
                f.Tracking,
                f.PayRunId,
                createdAt = f.CreatedAt,
                chargedAt = f.ChargedAt,
                package = f.Package == null ? null : new
                {
                    f.Package.Id,
                    f.Package.Tracking,
                    f.Package.Address,
                    f.Package.City,
                    f.Package.State,
                    f.Package.ZipCode,
                    status = f.Package.Status.ToString(),
                    f.Package.Weight
                }
            }).ToList();

            // ── Incidents with route + warehouse details ──────────────────────
            var incidentsList = await _db.Incidences
                .AsNoTracking()
                .Include(i => i.Route)
                    .ThenInclude(r => r!.Warehouse)
                .Where(i => i.UserId == driverId)
                .OrderByDescending(i => i.OccurredAt)
                .Select(i => new
                {
                    i.Id,
                    type = i.Type.ToString(),
                    i.Description,
                    i.ImageUrl,
                    occurredAt = i.OccurredAt,
                    createdAt = i.CreatedAt,
                    route = i.Route == null ? null : new
                    {
                        i.Route.Id,
                        i.Route.RouteCode,
                        date = i.Route.Date.ToString("yyyy-MM-dd"),
                        warehouse = i.Route.Warehouse == null ? null : new
                        {
                            i.Route.Warehouse.Id,
                            i.Route.Warehouse.Name,
                            i.Route.Warehouse.City,
                            i.Route.Warehouse.State
                        }
                    }
                })
                .ToListAsync(ct);

            // ── Loans with repayment history ─────────────────────────────────
            var loansRaw = await _db.EmployeeLoans
                .AsNoTracking()
                .Include(l => l.Repayments)
                .Where(l => l.DriverId == driverId)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync(ct);

            var loansList = loansRaw.Select(l => new
            {
                l.Id,
                l.Principal,
                l.Balance,
                l.InstallmentAmount,
                l.MaxDeductionPerPayRun,
                l.Status,
                l.Notes,
                createdAt = l.CreatedAt,
                approvedAt = l.ApprovedAt,
                repayments = l.Repayments
                    .OrderByDescending(r => r.AppliedAt)
                    .Select(r => new
                    {
                        r.Id,
                        r.Amount,
                        r.Status,
                        r.PayRunId,
                        appliedAt = r.AppliedAt,
                        reversedAt = r.ReversedAt,
                        r.Reason
                    }).ToList()
            }).ToList();

            // ── Active rental ────────────────────────────────────────────────
            var totalRentals = await _db.VehicleRentals
                .AsNoTracking()
                .CountAsync(r => r.RentalRenter != null && r.RentalRenter.UserId == driverId, ct);

            var activeRental = await _db.VehicleRentals
                .AsNoTracking()
                .Include(r => r.RentalVehicle)
                .Where(r => r.RentalRenter != null && r.RentalRenter.UserId == driverId &&
                            (r.Status == "Reserved" || r.Status == "Active"))
                .Select(r => new
                {
                    r.Id,
                    r.Status,
                    startDate = r.StartDate.ToString("yyyy-MM-dd"),
                    endDate = r.EndDate.ToString("yyyy-MM-dd"),
                    r.DailyPrice,
                    r.WeeklyPrice,
                    r.DepositAmount,
                    r.TotalAmount,
                    r.StartMileage,
                    r.Notes,
                    vehicle = r.RentalVehicle == null ? null : new
                    {
                        r.RentalVehicle.Id,
                        r.RentalVehicle.DisplayName,
                        r.RentalVehicle.Make,
                        r.RentalVehicle.Model,
                        r.RentalVehicle.Year,
                        r.RentalVehicle.Plate,
                        r.RentalVehicle.Color,
                        r.RentalVehicle.FuelType
                    }
                })
                .FirstOrDefaultAsync(ct);

            // ── Current pay rate ─────────────────────────────────────────────
            var currentRate = await _db.DriverRates
                .AsNoTracking()
                .Where(dr => dr.DriverId == driverId &&
                             (dr.EffectiveTo == null || dr.EffectiveTo >= today))
                .OrderByDescending(dr => dr.EffectiveFrom)
                .Select(dr => new
                {
                    dr.RateType,
                    dr.BaseAmount,
                    dr.DailyAmount,
                    dr.ExtraAmount,
                    dr.MinPayPerRoute,
                    effectiveFrom = dr.EffectiveFrom.ToString("yyyy-MM-dd"),
                    effectiveTo = dr.EffectiveTo.HasValue ? dr.EffectiveTo.Value.ToString("yyyy-MM-dd") : null
                })
                .FirstOrDefaultAsync(ct);

            // ── Payroll history (last 5 approved) ────────────────────────────
            var recentPayRuns = await _db.PayRuns
                .AsNoTracking()
                .Include(pr => pr.PayPeriod)
                .Include(pr => pr.AdjustmentsList)
                .Where(pr => pr.DriverId == driverId)
                .OrderByDescending(pr => pr.PayPeriod!.StartDate)
                .Take(5)
                .ToListAsync(ct);

            var payrollList = recentPayRuns.Select(pr => new
            {
                pr.Id,
                pr.Status,
                pr.GrossAmount,
                pr.PrepaidAmount,
                adjustmentsTotal = pr.Adjustments,
                pr.NetAmount,
                calculatedAt = pr.CalculatedAt,
                approvedAt = pr.ApprovedAt,
                period = pr.PayPeriod == null ? null : new
                {
                    pr.PayPeriod.Id,
                    startDate = pr.PayPeriod.StartDate.ToString("yyyy-MM-dd"),
                    endDate = pr.PayPeriod.EndDate.ToString("yyyy-MM-dd"),
                    pr.PayPeriod.Status
                },
                adjustments = pr.AdjustmentsList.Select(a => new
                {
                    a.Id,
                    a.Type,
                    a.Reason,
                    a.Amount,
                    a.RefType,
                    a.RefId,
                    createdAt = a.CreatedAt
                }).ToList()
            }).ToList();

            var totalApprovedEarnings = recentPayRuns
                .Where(pr => pr.Status == "Approved")
                .Sum(pr => pr.NetAmount);

            // ── Punches (all-time aggregate + last 30 days detail) ────────────
            var punchesAllTime = await _db.DriverPunches
                .AsNoTracking()
                .Where(p => p.DriverId == driverId)
                .OrderBy(p => p.OccurredAtUtc)
                .ToListAsync(ct);

            var punchByDay = punchesAllTime
                .GroupBy(p => p.OccurredAtUtc.Date)
                .Select(g =>
                {
                    var arr = g.FirstOrDefault(p => p.PunchType == PunchType.Arrival);
                    var dep = g.LastOrDefault(p => p.PunchType == PunchType.Departure);
                    double? hours = arr != null && dep != null && dep.OccurredAtUtc > arr.OccurredAtUtc
                        ? Math.Round((dep.OccurredAtUtc - arr.OccurredAtUtc).TotalHours, 2)
                        : (double?)null;
                    return new { date = g.Key, arr, dep, hours };
                })
                .ToList();

            var totalDaysWorked = punchByDay.Count;
            var totalHoursWorked = punchByDay.Where(d => d.hours.HasValue).Sum(d => d.hours!.Value);
            var avgHoursPerDay = totalDaysWorked > 0
                ? Math.Round(totalHoursWorked / totalDaysWorked, 2) : 0.0;
            var onTimePct = punchesAllTime.Count > 0
                ? Math.Round(punchesAllTime.Count(p => p.IsWithinGeofence) * 100.0 / punchesAllTime.Count, 1)
                : 0.0;

            var cutoff = DateTime.UtcNow.AddDays(-30).Date;
            var recentPunchDays = punchByDay
                .Where(d => d.date >= cutoff)
                .OrderByDescending(d => d.date)
                .Select(d => new
                {
                    date = d.date.ToString("yyyy-MM-dd"),
                    arrival = d.arr == null ? null : new
                    {
                        time = d.arr.OccurredAtUtc,
                        d.arr.IsWithinGeofence,
                        source = d.arr.Source.ToString(),
                        d.arr.Latitude,
                        d.arr.Longitude
                    },
                    departure = d.dep == null ? null : new
                    {
                        time = d.dep.OccurredAtUtc,
                        d.dep.IsWithinGeofence,
                        source = d.dep.Source.ToString(),
                        d.dep.Latitude,
                        d.dep.Longitude
                    },
                    hoursWorked = d.hours
                })
                .ToList();

            return Ok(new
            {
                driver = new
                {
                    user.Id,
                    fullName = $"{user.Name} {user.LastName}",
                    user.Email,
                    role = user.UserRole?.ToString(),
                    stage = user.Stage?.ToString(),
                    initialDate = user.InitialDate?.ToString("yyyy-MM-dd"),
                    confirmationDate = user.ConfirmationDate?.ToString("yyyy-MM-dd"),
                    daysWithCompany,
                    phone = user.Profile?.PhoneNumber,
                    address = user.Profile?.Address,
                    city = user.Profile?.City,
                    state = user.Profile?.State,
                    licenseNumber = user.Profile?.DriverLicenseNumber,
                    licenseExpiry = user.Profile?.ExpDriverLicense?.ToString("yyyy-MM-dd"),
                    warehouse = user.Warehouse == null ? null : new
                    {
                        user.Warehouse.Id,
                        user.Warehouse.Name,
                        user.Warehouse.City,
                        user.Warehouse.State
                    }
                },
                routeSummary = new
                {
                    totalRoutes   = routeStats?.TotalRoutes   ?? 0,
                    totalVolumen  = routeStats?.TotalVolumen  ?? 0,
                    totalDelivered = routeStats?.TotalDelivered ?? 0,
                    totalStops    = routeStats?.TotalStops    ?? 0,
                    lastRouteDate = routeStats?.LastRouteDate?.ToString("yyyy-MM-dd")
                },
                fines = new
                {
                    totalCount  = finesList.Count,
                    totalAmount = finesList.Sum(f => f.Amount),
                    items = finesList
                },
                incidents = new
                {
                    totalCount = incidentsList.Count,
                    items = incidentsList
                },
                loans = new
                {
                    totalCount   = loansList.Count,
                    activeCount  = loansList.Count(l => l.Status == "Active"),
                    totalBalance = loansList.Where(l => l.Status == "Active").Sum(l => l.Balance),
                    items = loansList
                },
                rental = new
                {
                    totalRentals,
                    active = activeRental
                },
                currentRate,
                payrollHistory = new
                {
                    totalApprovedEarnings,
                    items = payrollList
                },
                punches = new
                {
                    totalDaysWorked,
                    totalHours = Math.Round(totalHoursWorked, 2),
                    avgHoursPerDay,
                    onTimePercentage = onTimePct,
                    last30Days = recentPunchDays
                }
            });
        }

        // ─── Punches ────────────────────────────────────────────────────────────

        [HttpGet("driver/me/punches")]
        public async Task<IActionResult> GetMyPunches(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var driverId))
                return Unauthorized(new { message = "Invalid token. DriverId not found." });
            return await GetDriverPunchesInternal(driverId, from, to, ct);
        }

        [HttpGet("driver/{driverId:int}/punches")]
        public async Task<IActionResult> GetDriverPunches(
            int driverId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
            => await GetDriverPunchesInternal(driverId, from, to, ct);

        private async Task<IActionResult> GetDriverPunchesInternal(
            int driverId,
            DateTime? from,
            DateTime? to,
            CancellationToken ct)
        {
            var fromUtc = from?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-30);
            var toUtc = (to?.ToUniversalTime() ?? DateTime.UtcNow).Date.AddDays(1);

            var punches = await _db.DriverPunches
                .AsNoTracking()
                .Where(p => p.DriverId == driverId &&
                            p.OccurredAtUtc >= fromUtc &&
                            p.OccurredAtUtc < toUtc)
                .OrderBy(p => p.OccurredAtUtc)
                .ToListAsync(ct);

            // Group by calendar date and build arrival/departure pairs
            var byDay = punches
                .GroupBy(p => p.OccurredAtUtc.Date)
                .Select(g =>
                {
                    var arrival = g.FirstOrDefault(p => p.PunchType == PunchType.Arrival);
                    var departure = g.LastOrDefault(p => p.PunchType == PunchType.Departure);
                    double? hoursWorked = null;
                    if (arrival != null && departure != null && departure.OccurredAtUtc > arrival.OccurredAtUtc)
                        hoursWorked = Math.Round((departure.OccurredAtUtc - arrival.OccurredAtUtc).TotalHours, 2);

                    return new
                    {
                        date = g.Key.ToString("yyyy-MM-dd"),
                        arrival = arrival == null ? null : new
                        {
                            time = arrival.OccurredAtUtc,
                            arrival.IsWithinGeofence,
                            source = arrival.Source.ToString(),
                            arrival.Latitude,
                            arrival.Longitude
                        },
                        departure = departure == null ? null : new
                        {
                            time = departure.OccurredAtUtc,
                            departure.IsWithinGeofence,
                            source = departure.Source.ToString(),
                            departure.Latitude,
                            departure.Longitude
                        },
                        hoursWorked,
                        allPunches = g.Select(p => new
                        {
                            p.Id,
                            type = p.PunchType.ToString(),
                            p.OccurredAtUtc,
                            p.IsWithinGeofence,
                            source = p.Source.ToString(),
                            p.Notes
                        }).ToList()
                    };
                })
                .ToList();

            var daysWorked = byDay.Count;
            var totalHours = byDay.Where(d => d.hoursWorked.HasValue).Sum(d => d.hoursWorked!.Value);
            var avgHours = daysWorked > 0 ? Math.Round(totalHours / daysWorked, 2) : 0;
            var onTimePct = punches.Count > 0
                ? Math.Round(punches.Count(p => p.IsWithinGeofence) * 100.0 / punches.Count, 1)
                : 0;

            return Ok(new
            {
                days = byDay,
                totals = new
                {
                    daysWorked,
                    totalHours = Math.Round(totalHours, 2),
                    avgHoursPerDay = avgHours,
                    onTimePercentage = onTimePct
                }
            });
        }

        // ─── Fines ──────────────────────────────────────────────────────────────

        [HttpGet("driver/me/fines")]
        public async Task<IActionResult> GetMyFines(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var driverId))
                return Unauthorized(new { message = "Invalid token. DriverId not found." });
            return await GetDriverFinesInternal(driverId, from, to, ct);
        }

        [HttpGet("driver/{driverId:int}/fines")]
        public async Task<IActionResult> GetDriverFines(
            int driverId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
            => await GetDriverFinesInternal(driverId, from, to, ct);

        private async Task<IActionResult> GetDriverFinesInternal(
            int driverId,
            DateTime? from,
            DateTime? to,
            CancellationToken ct)
        {
            var fromDt = from?.Date ?? DateTime.MinValue;
            var toDt = (to?.Date ?? DateTime.MaxValue.Date).AddDays(1);

            var finesRaw = await _db.PayrollFines
                .AsNoTracking()
                .Include(f => f.Package)
                .Where(f => f.UserId == driverId &&
                            f.CreatedAt >= fromDt &&
                            f.CreatedAt < toDt)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync(ct);

            var fines = finesRaw.Select(f => new
            {
                f.Id,
                f.Type,
                f.Amount,
                f.Description,
                f.Tracking,
                f.PayRunId,
                createdAt = f.CreatedAt,
                chargedAt = f.ChargedAt,
                package = f.Package == null ? null : new
                {
                    f.Package.Id,
                    f.Package.Tracking,
                    f.Package.Address,
                    f.Package.City,
                    f.Package.State,
                    f.Package.ZipCode,
                    status = f.Package.Status.ToString(),
                    f.Package.Weight,
                    reviewStatus = f.Package.ReviewStatus.ToString()
                }
            }).ToList();

            var byType = fines
                .GroupBy(f => f.Type)
                .Select(g => new { type = g.Key, count = g.Count(), total = g.Sum(f => f.Amount) })
                .OrderByDescending(g => g.total)
                .ToList();

            return Ok(new
            {
                fines,
                summary = new
                {
                    totalCount = fines.Count,
                    totalAmount = fines.Sum(f => f.Amount),
                    byType
                }
            });
        }

        // ─── Incidents ──────────────────────────────────────────────────────────

        [HttpGet("driver/me/incidents")]
        public async Task<IActionResult> GetMyIncidents(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var driverId))
                return Unauthorized(new { message = "Invalid token. DriverId not found." });
            return await GetDriverIncidentsInternal(driverId, from, to, ct);
        }

        [HttpGet("driver/{driverId:int}/incidents")]
        public async Task<IActionResult> GetDriverIncidents(
            int driverId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
            => await GetDriverIncidentsInternal(driverId, from, to, ct);

        private async Task<IActionResult> GetDriverIncidentsInternal(
            int driverId,
            DateTime? from,
            DateTime? to,
            CancellationToken ct)
        {
            var fromDt = from?.Date ?? DateTime.MinValue;
            var toDt = (to?.Date ?? DateTime.MaxValue.Date).AddDays(1);

            var incidents = await _db.Incidences
                .AsNoTracking()
                .Include(i => i.Route)
                    .ThenInclude(r => r!.Warehouse)
                .Where(i => i.UserId == driverId &&
                            i.OccurredAt >= fromDt &&
                            i.OccurredAt < toDt)
                .OrderByDescending(i => i.OccurredAt)
                .Select(i => new
                {
                    i.Id,
                    type = i.Type.ToString(),
                    i.Description,
                    i.ImageUrl,
                    occurredAt = i.OccurredAt,
                    createdAt = i.CreatedAt,
                    route = i.Route == null ? null : new
                    {
                        i.Route.Id,
                        i.Route.RouteCode,
                        date = i.Route.Date.ToString("yyyy-MM-dd"),
                        status = i.Route.routeStatus.ToString(),
                        warehouse = i.Route.Warehouse == null ? null : new
                        {
                            i.Route.Warehouse.Id,
                            i.Route.Warehouse.Name,
                            i.Route.Warehouse.City,
                            i.Route.Warehouse.State
                        }
                    }
                })
                .ToListAsync(ct);

            var byType = incidents
                .GroupBy(i => i.type)
                .Select(g => new { type = g.Key, count = g.Count() })
                .OrderByDescending(g => g.count)
                .ToList();

            return Ok(new
            {
                incidents,
                summary = new
                {
                    totalCount = incidents.Count,
                    byType
                }
            });
        }

        // ─── Rentals ────────────────────────────────────────────────────────────

        [HttpGet("driver/me/rentals")]
        public async Task<IActionResult> GetMyRentals(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var driverId))
                return Unauthorized(new { message = "Invalid token. DriverId not found." });
            return await GetDriverRentalsInternal(driverId, from, to, ct);
        }

        [HttpGet("driver/{driverId:int}/rentals")]
        public async Task<IActionResult> GetDriverRentals(
            int driverId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
            => await GetDriverRentalsInternal(driverId, from, to, ct);

        private async Task<IActionResult> GetDriverRentalsInternal(
            int driverId,
            DateTime? from,
            DateTime? to,
            CancellationToken ct)
        {
            var fromDate = from.HasValue ? DateOnly.FromDateTime(from.Value) : DateOnly.MinValue;
            var toDate = to.HasValue ? DateOnly.FromDateTime(to.Value) : DateOnly.MaxValue;

            var rentals = await _db.VehicleRentals
                .AsNoTracking()
                .Include(r => r.RentalVehicle)
                .Where(r => r.RentalRenter != null && r.RentalRenter.UserId == driverId &&
                            r.StartDate <= toDate &&
                            r.EndDate >= fromDate)
                .OrderByDescending(r => r.StartDate)
                .Select(r => new
                {
                    r.Id,
                    r.Status,
                    startDate = r.StartDate.ToString("yyyy-MM-dd"),
                    endDate = r.EndDate.ToString("yyyy-MM-dd"),
                    r.DailyPrice,
                    r.WeeklyPrice,
                    r.DepositAmount,
                    r.TotalAmount,
                    r.StartMileage,
                    r.EndMileage,
                    r.Notes,
                    vehicle = r.RentalVehicle == null ? null : new
                    {
                        r.RentalVehicle.Id,
                        r.RentalVehicle.DisplayName,
                        r.RentalVehicle.Make,
                        r.RentalVehicle.Model,
                        r.RentalVehicle.Year,
                        r.RentalVehicle.Plate,
                        r.RentalVehicle.Color,
                        r.RentalVehicle.FuelType,
                        r.RentalVehicle.Transmission
                    }
                })
                .ToListAsync(ct);

            return Ok(new
            {
                rentals,
                summary = new
                {
                    totalRentals = rentals.Count,
                    totalAmount = rentals.Sum(r => r.TotalAmount),
                    totalDeposit = rentals.Sum(r => r.DepositAmount),
                    activeRentals = rentals.Count(r => r.Status == "Active" || r.Status == "Reserved")
                }
            });
        }

        // ─── Payroll History ────────────────────────────────────────────────────

        [HttpGet("driver/me/payroll-history")]
        public async Task<IActionResult> GetMyPayrollHistory(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var driverId))
                return Unauthorized(new { message = "Invalid token. DriverId not found." });
            return await GetDriverPayrollHistoryInternal(driverId, from, to, ct);
        }

        [HttpGet("driver/{driverId:int}/payroll-history")]
        public async Task<IActionResult> GetDriverPayrollHistory(
            int driverId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
            => await GetDriverPayrollHistoryInternal(driverId, from, to, ct);

        private async Task<IActionResult> GetDriverPayrollHistoryInternal(
            int driverId,
            DateTime? from,
            DateTime? to,
            CancellationToken ct)
        {
            var fromDate = from.HasValue ? DateOnly.FromDateTime(from.Value) : DateOnly.MinValue;
            var toDate = to.HasValue ? DateOnly.FromDateTime(to.Value) : DateOnly.MaxValue;

            var payRuns = await _db.PayRuns
                .AsNoTracking()
                .Include(pr => pr.PayPeriod)
                .Include(pr => pr.AdjustmentsList)
                .Include(pr => pr.Lines)
                .Where(pr => pr.DriverId == driverId &&
                             pr.PayPeriod != null &&
                             pr.PayPeriod.StartDate <= toDate &&
                             pr.PayPeriod.EndDate >= fromDate)
                .OrderByDescending(pr => pr.PayPeriod!.StartDate)
                .ToListAsync(ct);

            var result = payRuns.Select(pr => new
            {
                pr.Id,
                pr.Status,
                period = pr.PayPeriod == null ? null : new
                {
                    pr.PayPeriod.Id,
                    startDate = pr.PayPeriod.StartDate.ToString("yyyy-MM-dd"),
                    endDate = pr.PayPeriod.EndDate.ToString("yyyy-MM-dd"),
                    pr.PayPeriod.Status
                },
                pr.GrossAmount,
                pr.PrepaidAmount,
                adjustmentsTotal = pr.Adjustments,
                pr.NetAmount,
                calculatedAt = pr.CalculatedAt,
                approvedAt = pr.ApprovedAt,
                lines = pr.Lines.OrderBy(l => l.RouteDate).Select(l => new
                {
                    l.Id,
                    l.SourceType,
                    l.SourceId,
                    l.Description,
                    l.Qty,
                    l.Rate,
                    l.Amount,
                    l.Tags,
                    routeDate = l.RouteDate.HasValue ? l.RouteDate.Value.ToString("yyyy-MM-dd") : null,
                    l.ZoneArea,
                    l.ZoneId
                }).ToList(),
                adjustments = pr.AdjustmentsList.Select(a => new
                {
                    a.Id,
                    a.Type,
                    a.Reason,
                    a.Amount,
                    a.RefType,
                    a.RefId,
                    createdAt = a.CreatedAt
                }).ToList()
            }).ToList();

            return Ok(new
            {
                payRuns = result,
                summary = new
                {
                    totalPayRuns    = result.Count,
                    totalGross      = result.Sum(pr => pr.GrossAmount),
                    totalAdjustments = result.Sum(pr => pr.adjustmentsTotal),
                    totalNet        = result.Sum(pr => pr.NetAmount),
                    approvedCount   = result.Count(pr => pr.Status == "Approved"),
                    draftCount      = result.Count(pr => pr.Status == "Draft")
                }
            });
        }
    }
}