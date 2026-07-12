using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Services.Payroll
{
    public class PayrollService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<PayrollService> _logger;
        public PayrollService(ApplicationDbContext db, ILogger<PayrollService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public record FineSummary(decimal TotalFine, int PackagesCount);

        public async Task<PayRun> ComputeDriverWeeklyAsync(
            long companyId,
            long driverId,
            DateOnly weekStart,
            DateOnly weekEnd,
            long? warehouseId,
            long userId,
            int? filterZoneId = null
        )
        {
            var startDt = weekStart.ToDateTime(TimeOnly.MinValue);
            var endExclusive = weekEnd.AddDays(1).ToDateTime(TimeOnly.MinValue);

            // 1) PayPeriod
            var period = await _db.PayPeriods.FirstOrDefaultAsync(p =>
                p.CompanyId == companyId &&
                p.StartDate == weekStart &&
                p.EndDate == weekEnd &&
                p.WarehouseId == warehouseId
            );

            if (period is null)
            {
                period = new PayPeriod
                {
                    CompanyId = companyId,
                    WarehouseId = warehouseId,
                    StartDate = weekStart,
                    EndDate = weekEnd,
                    Status = "Open",
                    CreatedBy = userId
                };
                _db.PayPeriods.Add(period);
                await _db.SaveChangesAsync();
            }

            // 2) DriverRate vigente (solo BaseAmount por ahora)
            var rates = await _db.DriverRates
                .Where(r =>
                    r.DriverId == driverId &&
                   // r.EffectiveFrom <= weekEnd &&
                    (r.EffectiveTo == null || r.EffectiveTo >= weekStart))
                .OrderByDescending(r => r.WarehouseId != null) // específicos primero
                .ThenByDescending(r => r.EffectiveFrom)
                .ToListAsync();
                
            if (!rates.Any())
            {
                throw new Exception($"Driver {driverId} has no DriverRate configured.");
            }

            // 3) PayrollConfig (por warehouse)
            PayrollConfig? payrollConfig = null;
            List<PayrollWeightRule> weightRules = new();
            List<PayrollPenaltyRule> penaltyRules = new();
            List<PayrollBonusRule> bonustRules = new();
            bool isOnTrac = false;

            if (warehouseId.HasValue)
            {
                isOnTrac = await _db.Warehouses
                .AsNoTracking()
                .AnyAsync(w =>
                    w.Id == (int)warehouseId.Value &&
                    w.CompanyId == companyId &&          // si aplica en tu modelo
                    w.Company == "OnTrac"                // AJUSTA: o Contains("OnTrac")
                );
                payrollConfig = await _db.PayrollConfigs
                    .AsNoTracking()
                    .Include(x => x.WeightRules)
                    .Include(x => x.PenaltyRules)
                    .Include(x => x.BonusRules)
                    .FirstOrDefaultAsync(x => x.WarehouseId == (int)warehouseId.Value);                

                if (payrollConfig?.EnableWeightExtra == true)
                {
                    weightRules = payrollConfig.WeightRules
                        .Where(r => r.IsActive)
                        .OrderByDescending(r => r.Priority)
                        .ThenByDescending(r => r.MinWeight)
                        .ToList();
                }
                if (payrollConfig?.EnablePenalties == true)
                {
                    penaltyRules = payrollConfig.PenaltyRules
                        .Where(r => r.IsActive)
                        .OrderByDescending(r => r.Type)
                        .ThenByDescending(r => r.Amount)
                        .ToList();
                }
                if (payrollConfig?.EnableBonuses == true)
                {
                    bonustRules = payrollConfig.BonusRules
                        .Where(r => r.IsActive)
                        .OrderByDescending(r => r.Type)
                        .ThenByDescending(r => r.Amount)
                        .ToList();
                }
            }

            // 4) Rutas del driver en rango (Completed, Stops > 0)
            IQueryable<Routes> routesQuery = _db.Set<Routes>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r =>
                    r.routeStatus == RouteStatus.Completed &&
                    r.DeliveryStops > 0 &&
                    r.Date >= startDt &&
                    r.Date < endExclusive &&
                    r.UserId != null &&
                    r.UserId == (int)driverId
                );

            if (filterZoneId.HasValue && filterZoneId.Value > 0 && isOnTrac)
                routesQuery = routesQuery.Where(r => r.ZoneId == filterZoneId.Value);

            if (warehouseId.HasValue && isOnTrac)
            {
                var wid = (int)warehouseId.Value;
                routesQuery = routesQuery.Where(r =>
                    r.ZoneId != null &&
                    (r.WarehouseId == wid ||
                     (r.Zone != null && r.Zone.IdWarehouse == warehouseId.Value))
                );
            }
            if (warehouseId.HasValue && !isOnTrac)
            {
                var wid = (int)warehouseId.Value;
                routesQuery = routesQuery.Where(r => r.WarehouseId == wid);
            }


            var routes = await routesQuery
                .Include(r => r.Zone)
                .ToListAsync();


            DriverRate? GetDriverRateForRoute(Routes route)
            {
                var routeDate = DateOnly.FromDateTime(route.Date);
                var routeWarehouseId = route.WarehouseId ?? route.Zone?.IdWarehouse;

                var specificRate = rates
                    .Where(r =>
                        r.WarehouseId == routeWarehouseId &&
                        r.EffectiveFrom <= routeDate &&
                        (r.EffectiveTo == null || r.EffectiveTo >= routeDate))
                    .OrderByDescending(r => r.EffectiveFrom)
                    .FirstOrDefault();

                if (specificRate != null)
                    return specificRate;

                return rates
                    .Where(r =>
                        r.WarehouseId == null &&
                        r.EffectiveFrom <= routeDate &&
                        (r.EffectiveTo == null || r.EffectiveTo >= routeDate))
                    .OrderByDescending(r => r.EffectiveFrom)
                    .FirstOrDefault();
            }

            var hasRoutesInThisPayRun = routes.Any();
            // 5a) Cargar ZonePayRules para las zonas de las rutas
            var zoneIds = routes.Where(r => r.ZoneId.HasValue).Select(r => r.ZoneId!.Value).Distinct().ToList();
            List<ZonePayRule> zonePayRules = new();
            if (zoneIds.Any())
            {
                zonePayRules = await _db.Set<ZonePayRule>()
                    .AsNoTracking()
                    .Where(r =>
                        zoneIds.Contains(r.ZoneId) &&
                        r.IsActive &&
                        (r.EffectiveTo == null || r.EffectiveTo >= startDt))
                    .ToListAsync();
            }

            // 5b) Cargar ZoneWeightRules para las zonas de las rutas
            List<ZoneWeightRule> zoneWeightRules = new();
            if (zoneIds.Any())
            {
                zoneWeightRules = await _db.Set<ZoneWeightRule>()
                    .AsNoTracking()
                    .Where(r => zoneIds.Contains(r.ZoneId) && r.IsActive)
                    .OrderByDescending(r => r.Priority)
                    .ThenByDescending(r => r.MinWeight)
                    .ToListAsync();
            }

            // 5) Precargar pesos por ruta
            var routeIds = routes.Select(r => r.Id).Distinct().ToList();

            // 5c) Precargar bonos aprobados por ruta
            var approvedBonusesByRoute = (await _db.RouteBonuses
                .AsNoTracking()
                .Where(b => routeIds.Contains(b.RouteId) && b.IsActive && b.Status == RouteBonusStatus.Approved)
                .ToListAsync())
                .GroupBy(b => b.RouteId)
                .ToDictionary(g => g.Key, g => g.ToList());

            Dictionary<int, List<decimal>> weightsByRoute = new();

            var hasAnyWeightRules = weightRules.Count > 0 || zoneWeightRules.Count > 0;

            var packs = await _db.Set<Packages>()
                .AsNoTracking()
                .Where(p => p.RoutesId != null && routeIds.Contains((int)p.RoutesId))
                .Select(p => new
                {
                    RouteId = (int)p.RoutesId!,
                    Weight = p.Weight,
                    PackageId = p.Id
                })
                .ToListAsync();

            if (hasAnyWeightRules && routeIds.Count > 0)
            {
                weightsByRoute = packs
                    .Where(x => x.Weight.HasValue)
                    .GroupBy(x => x.RouteId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => x.Weight!.Value).ToList()
                    );
            }

            var payRun = await _db.PayRuns.FirstOrDefaultAsync(x => x.PayPeriodId == period.Id && x.DriverId == driverId);
            if (payRun is null)
            {
                payRun = new PayRun
                {
                    PayPeriodId = period.Id,
                    DriverId = (int)driverId,
                    Status = "Draft",
                    GrossAmount = 0m,
                    Adjustments = 0m
                };
                _db.PayRuns.Add(payRun);
                await _db.SaveChangesAsync();
            }
            else
            {
                var oldLines = _db.PayRunLines.Where(l => l.PayRunId == payRun.Id);
                _db.PayRunLines.RemoveRange(oldLines);
                // borra ajustes auto-generados (loans, etc.) pero preserva Manual y Penalty
                // los bonos de ruta (RefType=RouteBonus) se borran por separado
                var oldAdjs = _db.PayrollAdjustments
                .Where(a => a.PayRunId == payRun.Id && a.Type != "Manual" && a.Type != "Bonus" && a.Type != "Penalty");

                _db.PayrollAdjustments.RemoveRange(oldAdjs);

                // si ya habías aplicado repayments en otro cálculo, bórralos también
                var oldRepayments = await _db.LoanRepayments
                    .Where(r => r.PayRunId == payRun.Id && r.Status == "Applied")
                    .ToListAsync();

                var grouped = oldRepayments
                    .GroupBy(r => r.LoanId)
                    .Select(g => new
                    {
                        LoanId = g.Key,
                        TotalAmount = g.Sum(x => x.Amount)
                    })
                    .ToList();

                foreach (var item in grouped)
                {
                    var loan = await _db.EmployeeLoans
                        .FirstOrDefaultAsync(l => l.Id == item.LoanId);

                    if (loan != null)
                    {
                        loan.Balance += item.TotalAmount;
                    }
                }

                _db.LoanRepayments.RemoveRange(oldRepayments);

                await _db.SaveChangesAsync();
                payRun.AdjustmentsList.Clear();
            }

            decimal gross = 0m;
            var warnings = new List<string>();

            if (hasRoutesInThisPayRun)
            {
                var pendingFines = await _db.PayrollFines
                    .Where(f =>
                        f.UserId == driverId &&
                        f.Amount > 0 &&
                        f.ChargedAt == null &&
                        f.PayRunId == null)
                    .OrderBy(f => f.CreatedAt)
                    .ToListAsync();

                var totalPendingFines = pendingFines.Sum(f => f.Amount);

                if (totalPendingFines > 0)
                {
                    foreach (var fine in pendingFines)
                    {
                        gross += AddLine(
                            payRun,
                            "Fine",
                            fine.PackageId.ToString(),
                            $"Fine {fine.Type} - {fine.Tracking}",
                            1m,
                            -fine.Amount,
                            "FINE",
                            weekEnd.ToDateTime(TimeOnly.MinValue)
                        );
                    }

                    var now = DateTime.UtcNow;
                    foreach (var fine in pendingFines)
                    {
                        fine.ChargedAt = now;
                        fine.PayRunId = (int)payRun.Id;
                        fine.UpdatedAt = now;
                    }
                }
            }

            foreach (var route in routes)
            {
                var driverRate = GetDriverRateForRoute(route);
                var delivered = Math.Max(0, route.DeliveryStops - route.CNL);
                var failed    = Math.Max(0, route.CNL);
                var volumen   = route.Volumen;

                var activeZoneRule = route.ZoneId.HasValue
                    ? zonePayRules.FirstOrDefault(r =>
                        r.ZoneId == route.ZoneId.Value &&
                        r.IsActive &&
                        //r.EffectiveFrom <= route.Date &&
                        (r.EffectiveTo == null || r.EffectiveTo >= route.Date))
                    : null;

                var zoneRulePerStop = activeZoneRule?.PaymentType == PaymentType.PerStop;
                var zoneRuleSuffix = zoneRulePerStop ? $":ZONE_RULE:{activeZoneRule!.Id}" : "";

                decimal driverPerStop =
                    zoneRulePerStop
                        ? Math.Max(activeZoneRule!.BaseAmount ?? 0m, driverRate?.BaseAmount ?? 0m)
                        : driverRate?.RateType is "Mixed" or "PerStop"
                            ? driverRate.BaseAmount
                            : 0m;

                decimal zonePerStop =
                    route.Zone?.PriceStop ?? 0m;

                // Si la regla activa es PerStop, ella debe mandar sobre PriceStop
                if (zoneRulePerStop && activeZoneRule!.BaseAmount.HasValue)
                {
                    zonePerStop = activeZoneRule.BaseAmount.Value;
                }

                decimal effectivePerStop;
                string stopTag;

                if (zonePerStop > 0)
                {
                    effectivePerStop = isOnTrac
                        ? Math.Max(driverPerStop, zonePerStop)
                        : driverPerStop > 0 ? driverPerStop : zonePerStop;

                    stopTag = driverPerStop > zonePerStop
                        ? $"USE_DRIVER_BASE:{driverRate?.Id}{zoneRuleSuffix}"
                        : $"USE_ZONE_RATE{zoneRuleSuffix}";
                }
                else
                {
                    effectivePerStop = driverPerStop;

                    stopTag = route.Zone == null
                        ? "WARN_NO_ZONE"
                        : $"WARN_ZONE_PRICE_FALLBACK{zoneRuleSuffix}";
                }

                // ✅ 6.1) EXTRA POR PESO (por paquete)
                
                decimal routeSubtotal = 0m;
                var qtyExtraWeigth = 0m;

                var effectivePaymentType = activeZoneRule?.PaymentType ?? route.PaymentType;
                 
                // ✅ PAYMENT TYPE — se prioriza el ZonePayRule activo de la zona

                switch (effectivePaymentType)
                {
                    case PaymentType.PerRoute:
                        {
                            var priceRoute = activeZoneRule?.BaseAmount.HasValue == true
                                ? activeZoneRule.BaseAmount
                                : (decimal?)route.PriceRoute;
                            var perRouteTag = activeZoneRule?.BaseAmount.HasValue == true ? $"PAY_PER_ROUTE:ZONE_RULE:{activeZoneRule.Id}" : "PAY_PER_ROUTE";

                            if (priceRoute == null || priceRoute <= 0)
                            {
                                warnings.Add($"Ruta {route.Id}: PaymentType=PerRoute pero PriceRoute inválido ({priceRoute}); se pagó 0." );
                                AddLine(payRun, "Route", route.Id.ToString(),
                                    $"Route {route.Id} - {route.Date:yyyy-MM-dd} (PerRoute, sin precio)", 1m, 0m, "WARN_NO_ROUTE_PRICE",route.Date, route.Zone?.Id, route.Zone?.Area);
                            }
                            else
                            {
                                routeSubtotal += AddLine(payRun, "Route", route.Id.ToString(),
                                    $"Route {route.Id} - {route.Date:yyyy-MM-dd} (PerRoute)", 1m, priceRoute.Value, perRouteTag, route.Date, route.Zone?.Id, route.Zone?.Area);
                            }

                            break;
                        }

                    case PaymentType.PerStop:
                        {
                            if (delivered > 0)
                            {
                                if (weightsByRoute.TryGetValue(route.Id, out var weightsForRoute))
                                {
                                    // Prioridad: ZoneWeightRule de la zona > PayrollWeightRule del warehouse
                                    var routeZoneWeightRules = route.ZoneId.HasValue
                                        ? zoneWeightRules.Where(r => r.ZoneId == route.ZoneId.Value).ToList()
                                        : new List<ZoneWeightRule>();

                                    if (routeZoneWeightRules.Count > 0)
                                    {
                                        var extraByZoneRule = ComputeWeightExtras(weightsForRoute, routeZoneWeightRules);
                                        foreach (var item in extraByZoneRule)
                                        {
                                            var qty = item.Count;
                                            var rateExtra = Math.Max((item.Rule.ExtraAmount + (driverRate?.ExtraAmount ?? 0m)) + driverPerStop, zonePerStop);
                                            qtyExtraWeigth += qty;
                                            routeSubtotal += AddLine(
                                                payRun, "Earning", route.Id.ToString(),
                                                $"{route.Date:MMM dd, yyyy} - More than 1lb",
                                                qty, rateExtra, $"WEIGHT_EXTRA:ZONE_RULE:{item.Rule.Id}",
                                                route.Date, route.Zone?.Id, route.Zone?.Area);
                                        }
                                    }
                                    else if (weightRules.Count > 0)
                                    {
                                        var extraByRule = ComputeWeightExtras(weightsForRoute, weightRules);
                                        foreach (var item in extraByRule)
                                        {
                                            var qty = item.Count;
                                            var rateExtra = Math.Max((item.Rule.ExtraAmount + (driverRate?.ExtraAmount ?? 0m)) + driverPerStop, zonePerStop);
                                            qtyExtraWeigth += qty;
                                            routeSubtotal += AddLine(
                                                payRun, "Earning", route.Id.ToString(),
                                                $"{route.Date:MMM dd, yyyy} - More than 1lb",
                                                qty, rateExtra, "WEIGHT_EXTRA",
                                                route.Date, route.Zone?.Id, route.Zone?.Area);
                                        }
                                    }
                                }

                                routeSubtotal += AddLine(
                                payRun,
                                "Earning",
                                route.Id.ToString(), 
                                $"{route.Date:MMM dd, yyyy}  {(route.Zone != null ? $"Zone {route.Zone.ZoneCode} " : "")}- PerStop",
                                (delivered - qtyExtraWeigth) > 0 ? (delivered - qtyExtraWeigth) : 0m,
                                effectivePerStop,
                                stopTag,
                                route.Date,
                                route.Zone?.Id,
                                route.Zone?.Area
                            );
                            }
                            else
                            {
                                AddLine(payRun, "Stop", route.Id.ToString(),
                                    "Delivered Stops 0 (PerStop)", 0m, effectivePerStop, "INFO_ZERO_DELIVERED",route.Date, route.Zone?.Id, route.Zone?.Area);
                            }

                            break;
                        }

                    case PaymentType.PerBlock:
                        {
                            if (delivered > 0)
                            {
                                if (activeZoneRule?.BaseAmount.HasValue == true)
                                {
                                    routeSubtotal += AddLine(
                                        payRun, "Earning", route.Id.ToString(),
                                        $"{route.Date:MMM dd, yyyy} - Block ({activeZoneRule.MaxPackages} pkgs, Zone {route.Zone?.ZoneCode})",
                                        1m, activeZoneRule.BaseAmount.Value, $"ZONE_BLOCK_RATE:ZONE_RULE:{activeZoneRule.Id}",
                                        route.Date, route.Zone?.Id, route.Zone?.Area);

                                    var excess = delivered - (activeZoneRule.MaxPackages ?? 0);
                                    if (excess > 0)
                                    {
                                        var extraRate = activeZoneRule?.ExtraAmount ?? 0m;
                                        var extraTag = "ZONE_BLOCK_EXTRA";

                                        routeSubtotal += AddLine(
                                            payRun, "Earning", route.Id.ToString(),
                                            $"{route.Date:MMM dd, yyyy} - Extra pkgs beyond block",
                                            excess, extraRate, extraTag,
                                            route.Date, route.Zone?.Id, route.Zone?.Area);
                                    }
                                }
                                else
                                {
                                    warnings.Add($"Route {route.Id}: ZonePayRule PerBlock sin BaseAmount configurado en zona {route.ZoneId}.");
                                    routeSubtotal += AddLine(
                                        payRun, "Earning", route.Id.ToString(),
                                        $"{route.Date:MMM dd, yyyy} {(route.Zone != null ? $"Zone {route.Zone.ZoneCode} " : "")} - PerBlock fallback",
                                        delivered, effectivePerStop, stopTag,
                                        route.Date, route.Zone?.Id, route.Zone?.Area);
                                }
                            }
                            else
                            {
                                AddLine(payRun, "Stop", route.Id.ToString(),
                                    "Delivered Stops 0 (PerBlock)", 0m, effectivePerStop, "INFO_ZERO_DELIVERED",
                                    route.Date, route.Zone?.Id, route.Zone?.Area);
                            }
                            break;
                        }

                    case PaymentType.PerStopPlusAdditionalPackage:
                        {
                            if (delivered > 0)
                            {
                                var stopRate   = activeZoneRule?.BaseAmount ?? effectivePerStop;
                                var extraRate  = activeZoneRule.UseDriverRateForExtra
                                            ? effectivePerStop
                                            : activeZoneRule?.ExtraAmount ?? 0m;
                                var diff       = Math.Max(0m, (decimal)route.Volumen - delivered);
                                var ruleTag    = activeZoneRule != null ? $":ZONE_RULE:{activeZoneRule.Id}" : $":DRIVER_RATE:{driverRate?.Id}";

                                routeSubtotal += AddLine(
                                    payRun, "Earning", route.Id.ToString(),
                                    $"{route.Date:MMM dd, yyyy} - PerStop ({delivered} stops)",
                                    delivered, stopRate, $"PAY_PER_STOP_{ruleTag}",
                                    route.Date, route.Zone?.Id, route.Zone?.Area);

                                if (diff > 0)
                                    routeSubtotal += AddLine(
                                        payRun, "Earning", route.Id.ToString(),
                                        $"{route.Date:MMM dd, yyyy} - Pkg diff ({diff} extra pkgs)",
                                        diff, extraRate, $"PAY_PKG_DIFF{ruleTag}",
                                        route.Date, route.Zone?.Id, route.Zone?.Area);
                            }
                            else
                            {
                                AddLine(payRun, "Stop", route.Id.ToString(),
                                    "Delivered Stops 0 (PerStopPlusAdditionalPackage)", 0m, 0m, "INFO_ZERO_DELIVERED",
                                    route.Date, route.Zone?.Id, route.Zone?.Area);
                            }
                            break;
                        }

                    // case PaymentType.Mixed:
                    // default:
                    //     {
                    //         var priceRoute = route.PriceRoute;

                    //         if (priceRoute > 0)
                    //             routeSubtotal += AddLine(payRun, "Route", route.Id.ToString(),
                    //                 $"Route {route.Id} - {route.Date:yyyy-MM-dd} (Mixed-Route)", 1m, (decimal)priceRoute, "PAY_MIXED_ROUTE",route.Date, route.Zone?.Id, route.Zone?.Area);

                    //         if (delivered > 0)
                    //             routeSubtotal += AddLine(payRun, "Stop", route.Id.ToString(),
                    //                 "Delivered Stops (Mixed-Stop)", delivered, effectivePerStop, "PAY_MIXED_STOP",route.Date, route.Zone?.Id, route.Zone?.Area);

                    //         break;
                    //     }
                }

                // Penalidad CNL (si aplica)
                if (failed > 0 && driverRate?.FailedStopPenalty.GetValueOrDefault() > 0)
                {
                    routeSubtotal += AddLine(payRun, "Stop", route.Id.ToString(),
                        "CNL Penalty", failed, -driverRate!.FailedStopPenalty!.Value, "CNL_PENALTY", route.Date, route.Zone?.Id, route.Zone?.Area);
                }

                // Mínimo por ruta (si aplica)
                if (driverRate?.MinPayPerRoute.HasValue == true && routeSubtotal < driverRate.MinPayPerRoute.Value)
                {
                    var diff = driverRate.MinPayPerRoute.Value - routeSubtotal;
                    routeSubtotal += AddLine(payRun, "Bonus", route.Id.ToString(),
                        "Minimum adjustment per route", 1m, diff, "MIN_ROUTE_ADJUST", route.Date, route.Zone?.Id, route.Zone?.Area);
                }

                gross += routeSubtotal;
                
                // Penalidades por multas asociadas a la ruta 

                /*if (penaltyRules?.Count > 0 && finesByRoute.TryGetValue(route.Id, out var fine))
                {
                    var penaltyRule = penaltyRules[0]; 
                    var packagesWithFine = fine.PackagesCount;
                    var penaltyAmount = penaltyRule.ApplyPerOccurrence
                        ? (penaltyRule.Amount > 0
                            ? penaltyRule.Amount * packagesWithFine
                            : fine.TotalFine)
                        : penaltyRule.Amount;

                    if (penaltyAmount > 0)
                    {
                        gross += AddLine(
                            payRun,
                            "Fine",
                            route.Id.ToString(),
                            "Penalties applied",
                            1m,
                            -penaltyAmount,
                            "FINE_APPLIED",
                            route.Date,
                            route.Zone?.Id,
                            route.Zone?.Area
                        );
                    }
                }*/
            }
           var mixedRatesWithDaily = rates
            .Where(r =>
                r.RateType == "Mixed" &&
                r.DailyAmount.GetValueOrDefault() > 0 &&
                r.WarehouseId.HasValue)
            .ToList();

            if (mixedRatesWithDaily.Any())
            {
                var mixedWarehouseIds = mixedRatesWithDaily
                    .Select(r => r.WarehouseId!.Value)
                    .Distinct()
                    .ToList();

                var startUtc = weekStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var endExclusiveUtc = weekEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

                var punchDaysByWarehouse = await _db.DriverPunches
                    .AsNoTracking()
                    .Where(p =>
                        p.DriverId == driverId &&
                        mixedWarehouseIds.Contains(p.WarehouseId) &&
                        p.OccurredAtUtc >= startUtc &&
                        p.OccurredAtUtc < endExclusiveUtc)
                    .Select(p => new
                    {
                        Day = DateOnly.FromDateTime(p.OccurredAtUtc),
                        p.WarehouseId
                    })
                    .Distinct()
                    .OrderBy(x => x.Day)
                    .ThenBy(x => x.WarehouseId)
                    .ToListAsync();

                foreach (var punchDay in punchDaysByWarehouse)
                {
                    var dailyRateObj = mixedRatesWithDaily
                        .Where(r =>
                            r.WarehouseId == punchDay.WarehouseId &&
                            r.EffectiveFrom <= punchDay.Day &&
                            (r.EffectiveTo == null || r.EffectiveTo >= punchDay.Day))
                        .OrderByDescending(r => r.EffectiveFrom)
                        .FirstOrDefault();

                    if (dailyRateObj == null)
                        continue;

                    var dailyRate = dailyRateObj.DailyAmount.GetValueOrDefault();

                    AddLine(
                        payRun,
                        "DailyAmount",
                        driverId.ToString(),
                        $"Daily Amount on: {punchDay.Day:MMM dd, yyyy} - WH {punchDay.WarehouseId}",
                        1m,
                        dailyRate,
                        "DAILY_AMOUNT",
                        punchDay.Day.ToDateTime(TimeOnly.MinValue)
                    );

                    gross += dailyRate;
                }
            }
           

            // if (routeRate.RateType == "Mixed" && routeRate.DailyAmount > 0) {
 
            //     var startUtc = weekStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            //     var endExclusiveUtc = weekEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            //     var days = await _db.DriverPunches
            //         .AsNoTracking()
            //         .Where(p => p.DriverId == driverId)
            //         .Where(p => p.OccurredAtUtc >= startUtc && p.OccurredAtUtc < endExclusiveUtc)
            //         .Select(p => DateOnly.FromDateTime(p.OccurredAtUtc))
            //         .Distinct()
            //         .OrderBy(d => d)
            //         .ToListAsync();
            //     var dailyRate = rate.DailyAmount.GetValueOrDefault();
            //     foreach (var day in days)
            //     {
            //         AddLine(
            //             payRun,
            //             "DailyAmount",
            //             driverId.ToString(),
            //             $"Daily Amount on: {day:MMM dd, yyyy}" ,
            //             1m,
            //             dailyRate,
            //             "DAILY_AMOUNT",
            //             day.ToDateTime(TimeOnly.MinValue)
                        
            //         );
            //     }
            //     gross += days.Count * dailyRate;
            // }
            
            if (warnings.Count > 0)
                AddLine(payRun, "Info", null, $"Warnings: {warnings.Count}", 0m, 0m, "WARN_SUMMARY");

            // Bonos aprobados → se suman a gross y se registran como PayRunLine
            foreach (var (routeId, bonuses) in approvedBonusesByRoute)
            {
                var route = routes.FirstOrDefault(r => r.Id == routeId);
                foreach (var bonus in bonuses)
                {
                    gross += AddLine(payRun, "Bonus", routeId.ToString(),
                        $"Route bonus: {bonus.Type}",
                        1m, bonus.Amount, $"ROUTE_BONUS:{bonus.Id}:{bonus.Type}",
                        route?.Date, route?.Zone?.Id, route?.Zone?.Area);
                }
            }

            payRun.GrossAmount = gross;

            await ApplyLoanDeductionsAsync(payRun, userId);
            await _db.SaveChangesAsync();

        //2) recalcula Adjustments total (incluye loan deductions y bonos)
            payRun.Adjustments = await _db.PayrollAdjustments
                .Where(a => a.PayRunId == payRun.Id)
                .SumAsync(a => (decimal?)a.Amount) ?? 0m;

            payRun.CalculatedAt = DateTime.UtcNow;
            payRun.CalculatedBy = userId;
                
            await _db.SaveChangesAsync();
            return payRun;
        }

        private async Task ApplyLoanDeductionsAsync(PayRun payRun, long userId)
        {

            // net disponible antes de préstamos:
            // En tu diseño net = Gross + Adjustments, y estamos recalculando adjustments luego.
            // Aquí solo usamos "capacidad" = Gross + ajustes NO loan (pero en este punto ya borramos ajustes).
            var netAvailable = payRun.GrossAmount; // asumiendo que no quieres dejar net negativo


            // trae préstamos activos con saldo
            var loans = await _db.EmployeeLoans
                .Where(l => l.DriverId == payRun.DriverId
                    && l.Status == "Active"
                    && l.Balance > 0)
                .OrderBy(l => l.CreatedAt) // FIFO
                .ToListAsync();

            foreach (var loan in loans)
            {
                if (netAvailable <= 0) break;

                // desired: cuota fija o máximo por corrida o saldo completo
                decimal desired = loan.InstallmentAmount;

                if (desired <= 0)
                    desired = (decimal)loan.MaxDeductionPerPayRun;

                if (desired <= 0)
                    desired = loan.Balance;

                // si hay MaxDeductionPerPayRun, respeta el menor
                if (loan.MaxDeductionPerPayRun.HasValue)
                    desired = Math.Min(desired, loan.MaxDeductionPerPayRun.Value);

                var amount = Math.Min(desired, loan.Balance);
                amount = Math.Min(amount, netAvailable); // evita net negativo

                if (amount <= 0) continue;

                // 1) Adjustment negativo en payroll
                _db.PayrollAdjustments.Add(new PayrollAdjustment
                {
                    PayRunId = payRun.Id,
                    Type = "LoanRepayment",
                    Reason = $"Loan repayment (Loan #{loan.Id})",
                    Amount = -amount,
                    CreatedBy = userId,
                    CreatedAt = DateTime.UtcNow
                    // (opcional) RefType/RefId si los agregaste
                });

                // 2) Auditoría del cobro
                _db.LoanRepayments.Add(new LoanRepayment
                {
                    LoanId = loan.Id,
                    PayRunId = payRun.Id,
                    DriverId = payRun.DriverId,
                    Amount = amount,
                    AppliedAt = DateTime.UtcNow,
                    AppliedBy = userId,
                    Status = "Applied"
                });

                // 3) reduce saldo
                loan.Balance -= amount;

                if (loan.Balance == 0)
                    loan.Status = "Completed";

                netAvailable -= amount;

                AddLine(
                        payRun,
                        "LoanDeduction",
                        payRun.DriverId.ToString(),
                        $"Loan Deduction for Loan #{loan.Id}" ,
                        1m,
                        -amount,
                        "LOAN_DEDUCTION",
                        null
                    );
            }

        }


        /// <summary>
        /// Devuelve extras agrupados por regla:
        /// - Por cada weight, toma la primera regla que matchee (ordenadas por Priority desc, MinWeight desc)
        /// - Agrupa para generar líneas agregadas (qty por regla).
        /// </summary>
        private static List<(PayrollWeightRule Rule, decimal Count)> ComputeWeightExtras(
            List<decimal> weights,
            List<PayrollWeightRule> rules
        )
        {
            // key: ruleId, value: count
            var counts = new Dictionary<int, (PayrollWeightRule Rule, decimal Count)>();

            foreach (var w in weights)
            {
                var rule = FindRuleForWeight(w, rules);
                if (rule == null) continue;

                if (!counts.TryGetValue(rule.Id, out var entry))
                    counts[rule.Id] = (rule, 1m);
                else
                    counts[rule.Id] = (entry.Rule, entry.Count + 1m);
            }

            return counts.Values
                .OrderByDescending(x => x.Rule.Priority)
                .ThenByDescending(x => x.Rule.MinWeight)
                .Select(x => (x.Rule, x.Count))
                .ToList();
        }

        private static PayrollWeightRule? FindRuleForWeight(decimal weight, List<PayrollWeightRule> rules)
        {
            foreach (var r in rules)
            {
                if (weight < r.MinWeight) continue;
                if (r.MaxWeight.HasValue && weight > r.MaxWeight.Value) continue;
                return r;
            }
            return null;
        }

        private static List<(ZoneWeightRule Rule, decimal Count)> ComputeWeightExtras(
            List<decimal> weights,
            List<ZoneWeightRule> rules)
        {
            var counts = new Dictionary<int, (ZoneWeightRule Rule, decimal Count)>();

            foreach (var w in weights)
            {
                var rule = rules.FirstOrDefault(r =>
                    w >= r.MinWeight &&
                    (!r.MaxWeight.HasValue || w <= r.MaxWeight.Value));

                if (rule == null) continue;

                if (!counts.TryGetValue(rule.Id, out var entry))
                    counts[rule.Id] = (rule, 1m);
                else
                    counts[rule.Id] = (entry.Rule, entry.Count + 1m);
            }

            return counts.Values
                .OrderByDescending(x => x.Rule.Priority)
                .ThenByDescending(x => x.Rule.MinWeight)
                .Select(x => (x.Rule, x.Count))
                .ToList();
        }

        private decimal AddLine(
            PayRun run,
            string sourceType,
            string? sourceId,
            string? description,
            decimal qty,
            decimal rate,
            string? tags = null,
            DateTime? routeDate = null,
            long? zoneId = null,
            string? zoneArea = null

        )
        {
            var amount = qty * rate;
            var line = new PayRunLine
            {
                PayRunId = run.Id,
                SourceType = sourceType,
                SourceId = sourceId,
                Description = description,
                Qty = qty,
                Rate = rate,
                Tags = tags,
                RouteDate = routeDate,
                ZoneId = zoneId,
                ZoneArea = zoneArea
            };

            _db.PayRunLines.Add(line);
            return qty * rate;
        }

        public Task<PayRun> ComputeDriverWeeklyAsync(
            long companyId,
            long driverId,
            DateTime startDateInclusive,
            DateTime endDateInclusive,
            long? warehouseId,
            long userId,
            int? zoneId = null
        )
        {
            var start = DateOnly.FromDateTime(startDateInclusive.Date);
            var end = DateOnly.FromDateTime(endDateInclusive.Date);
            return ComputeDriverWeeklyAsync(companyId, driverId, start, end, warehouseId, userId, zoneId);
        }
    }
}
