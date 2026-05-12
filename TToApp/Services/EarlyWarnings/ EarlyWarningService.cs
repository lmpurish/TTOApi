using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TToApp.Constants;
using TToApp.Model;

namespace TToApp.Services.EarlyWarnings
{

    public class EarlyWarningService : IEarlyWarningService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<EarlyWarningNotificationService> _logger;

        public EarlyWarningService(ApplicationDbContext db,  ILogger<EarlyWarningNotificationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task CheckHiringCapacityAsync(DateOnly? referenceDateAux )
        {

          var warehouses = await _db.Warehouses
            .Where(w => w.IsHiring && w.CompanyId != null)
            .Select(w => new
            {
                CompanyId = w.CompanyId!.Value,
                WarehouseId = w.Id
            })
            .ToListAsync();
            
            foreach (var wh in warehouses)
            {
                var lastRouteDate = await GetLastRouteDateAsync(wh.WarehouseId);

                if (lastRouteDate == null)
                    continue;

                //await CheckHiringCapacityAsync(lastRouteDate.Value);
                var referenceDate = referenceDateAux ?? lastRouteDate.Value ;
                var config = await _db.EarlyWarningConfigs
                    .Where(c =>
                        c.CompanyId == wh.CompanyId &&
                        c.Type == EarlyWarningTypes.HiringCapacity &&
                        c.IsActive &&
                        (c.WarehouseId == wh.WarehouseId || c.WarehouseId == null))
                    .OrderByDescending(c => c.WarehouseId != null) // prioridad específica
                    .FirstOrDefaultAsync();

                if (config == null)
                    continue;

                var thresholdPercent = config.ThresholdPercent;
                var daysForCritical = config.DaysForCritical;

                var lastPeriod = await _db.PayPeriods
                    .Where(p =>
                        p.CompanyId == wh.CompanyId &&
                        p.WarehouseId == wh.WarehouseId &&
                        p.EndDate < referenceDate)
                    .OrderByDescending(p => p.EndDate)
                    .FirstOrDefaultAsync();

                if (lastPeriod == null)
                    continue;

                var baseline = await GetPackagesPerDriverDayAsync(
                    wh.WarehouseId,
                    lastPeriod.StartDate,
                    lastPeriod.EndDate
                );

                if (baseline.AvgPackagesPerDriverDay <= 0)
                    continue;

                // WARNING: solo ayer
                await EvaluateAndCreateWarningAsync(
                    companyId: wh.CompanyId,
                    warehouseId: wh.WarehouseId,
                    referenceDate: referenceDate,
                    daysEvaluated: 1,
                    level: EarlyWarningLevels.Warning,
                    baseline: baseline,
                    startDate: referenceDate,
                    endDate: referenceDate,
                    thresholdPercent: thresholdPercent
                );

                // CRITICAL: promedio últimos 3 días

                var criticalStart = referenceDate.AddDays(-(daysForCritical - 1));

                await EvaluateAndCreateWarningAsync(
                    companyId: wh.CompanyId,
                    warehouseId: wh.WarehouseId,
                    referenceDate: referenceDate,
                    daysEvaluated: daysForCritical,
                    level: EarlyWarningLevels.Critical,
                    baseline: baseline,
                    startDate: criticalStart,
                    endDate: referenceDate,
                    thresholdPercent: thresholdPercent
                );
            }

            await _db.SaveChangesAsync();
        }                   

        private async Task<DateOnly?> GetLastRouteDateAsync(long? warehouseId)
        {
            var lastDate = await _db.Routes
                .Where(r =>
                    r.WarehouseId == warehouseId &&
                    r.UserId != null &&
                    r.DeliveryStops > 0)
                .OrderByDescending(r => r.Date)
                .Select(r => (DateTime?)r.Date)
                .FirstOrDefaultAsync();

            return lastDate == null
                ? null
                : DateOnly.FromDateTime(lastDate.Value);
        }

        private async Task EvaluateAndCreateWarningAsync(
            long companyId,
            int? warehouseId,
            DateOnly referenceDate,
            int daysEvaluated,
            string level,
            WarehousePackageStats baseline,
            DateOnly startDate,
            DateOnly endDate,
            decimal thresholdPercent)
        {
            var current = await GetPackagesPerDriverDayAsync(
                warehouseId,
                startDate,
                endDate
            );

            if (current.AvgPackagesPerDriverDay <= 0)
                return;

            var increasePercent =
                ((current.AvgPackagesPerDriverDay - baseline.AvgPackagesPerDriverDay)
                    / baseline.AvgPackagesPerDriverDay) * 100m;

            if (increasePercent < thresholdPercent)
                return;

            var exists = await _db.EarlyWarnings.AnyAsync(x =>
                x.CompanyId == companyId &&
                x.WarehouseId == warehouseId &&
                x.Type == EarlyWarningTypes.HiringCapacity &&
                x.ReferenceDate == referenceDate &&
                x.DaysEvaluated == daysEvaluated
            );

            if (exists)
                return;

            var payload = new
            {
                baselinePeriodPackages = baseline.TotalPackages,
                baselinePeriodDriverDays = baseline.TotalDriverDays,
                baselineAvgPackagesPerDriverDay = baseline.AvgPackagesPerDriverDay,

                currentPackages = current.TotalPackages,
                currentDriverDays = current.TotalDriverDays,
                currentAvgPackagesPerDriverDay = current.AvgPackagesPerDriverDay,

                startDate,
                endDate,
                thresholdPercent
            };

            var warning = new EarlyWarning
            {
                CompanyId = companyId,
                WarehouseId = warehouseId,
                Type = EarlyWarningTypes.HiringCapacity,
                Level = level,
                ReferenceDate = referenceDate,
                DaysEvaluated = daysEvaluated,

                BaselineValue = Math.Round(baseline.AvgPackagesPerDriverDay, 2),
                CurrentValue = Math.Round(current.AvgPackagesPerDriverDay, 2),
                IncreasePercent = Math.Round(increasePercent, 2),

                Status = "Open",

                Message =
                    $"Warehouse {warehouseId} The measurement exceeded the average of the last PayPeriod by {increasePercent:F2}%. " +
                    $"Previous average: {baseline.AvgPackagesPerDriverDay:F2}, " +
                    $"Current average: {current.AvgPackagesPerDriverDay:F2}.",

                PayloadJson = JsonSerializer.Serialize(payload),

                CreatedAt = DateTime.UtcNow
            };

            _db.EarlyWarnings.Add(warning);
        }

        private async Task<WarehousePackageStats> GetPackagesPerDriverDayAsync(
            int? warehouseId,
            DateOnly startDate,
            DateOnly endDate)
        {
            var start = startDate.ToDateTime(TimeOnly.MinValue);
            var endExclusive = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var daily = await _db.Routes
                .Where(r =>
                    r.WarehouseId == warehouseId &&
                    r.UserId != null &&
                    r.DeliveryStops > 0 &&
                    r.Date >= start &&
                    r.Date < endExclusive
                    // && r.routeStatus == RouteStatus.Completed
                )
                .GroupBy(r => r.Date.Date)
                .Select(g => new
                {
                    Packages = g.Sum(x => x.DeliveryStops),
                    Drivers = g.Select(x => x.UserId).Distinct().Count()
                })
                .ToListAsync();

            var totalPackages = daily.Sum(x => x.Packages);
            var totalDriverDays = daily.Sum(x => x.Drivers);

            var avg = totalDriverDays == 0
                ? 0
                : (decimal)totalPackages / totalDriverDays;

            return new WarehousePackageStats
            {
                TotalPackages = totalPackages,
                TotalDriverDays = totalDriverDays,
                AvgPackagesPerDriverDay = avg
            };
        }

        private class WarehousePackageStats
        {
            public int TotalPackages { get; set; }
            public int TotalDriverDays { get; set; }
            public decimal AvgPackagesPerDriverDay { get; set; }
        }
        public async Task CheckMissingDailyPackagesAsync(DateOnly referenceDate)
        {
            var warehouses = await _db.Warehouses
               .Where(w => w.IsHiring && w.CompanyId != null)
              // .Where(w => w.Id == 5)
                .Select(w => new
                {
                    CompanyId = w.CompanyId!.Value,
                    WarehouseId = w.Id,
                    Name = w.City + ", " + w.State
                })
                .ToListAsync();

            foreach (var wh in warehouses)
            {
                var start = referenceDate.ToDateTime(TimeOnly.MinValue);
                var end = referenceDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

                var hasPackages = await _db.Routes.AnyAsync(r =>
                    r.WarehouseId == wh.WarehouseId &&
                    r.Date >= start &&
                    r.Date < end &&
                    r.UserId != null &&
                    r.DeliveryStops > 0
                );

                if (hasPackages)
                    continue;

                var exists = await _db.EarlyWarnings.AnyAsync(x =>
                    x.CompanyId == wh.CompanyId &&
                    x.WarehouseId == wh.WarehouseId &&
                    x.Type == EarlyWarningTypes.MissingDailyPackages &&
                    x.ReferenceDate == referenceDate
                );

                if (exists)
                    continue;

                _db.EarlyWarnings.Add(new EarlyWarning
                {
                    CompanyId = wh.CompanyId,
                    WarehouseId = wh.WarehouseId,
                    Type = EarlyWarningTypes.MissingDailyPackages,
                    Level = EarlyWarningLevels.Warning,
                    ReferenceDate = referenceDate,
                    DaysEvaluated = 1,
                    BaselineValue = 1,
                    CurrentValue = 0,
                    IncreasePercent = null,
                    Status = "Open",
                    Message = $"Have no packages uploaded for the warehouse {wh.Name} on the date {referenceDate:yyyy-MM-dd}.",
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        warehouseId = wh.WarehouseId,
                        warehouse = wh.Name,
                        missingDate = referenceDate
                    }),
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
        }
    }
}