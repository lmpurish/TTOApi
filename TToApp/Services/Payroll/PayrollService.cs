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
                    r.EffectiveFrom <= weekEnd &&
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

            if (filterZoneId.HasValue && isOnTrac)
                routesQuery = routesQuery.Where(r => r.ZoneId == filterZoneId.Value);

            if (warehouseId.HasValue && isOnTrac)
            {
                // OnTrac: requiere zona y amarra el warehouse a la zona
                routesQuery = routesQuery.Where(r =>
                    r.ZoneId != null &&
                    r.Zone != null &&
                    r.Zone.IdWarehouse == warehouseId.Value
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

                DriverRate? GetRateForRoute(Routes route)
                {
                    var routeDate = DateOnly.FromDateTime(route.Date);
                    var routeWarehouseId = route.WarehouseId;

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

            // 5) Precargar pesos por ruta (solo si weightRules aplica)
            var routeIds = routes.Select(r => r.Id).Distinct().ToList(); // Id de Routes (int)

            Dictionary<int, List<decimal>> weightsByRoute = new();
              
            // 🔧 get all RoutesId/Weight and PackageId per route

            var packs = await _db.Set<Packages>()   // <-- cambia si tu entidad real es Package
                .AsNoTracking()
                .Where(p => p.RoutesId != null && routeIds.Contains((int)p.RoutesId))
                .Select(p => new
                {
                    RouteId = (int)p.RoutesId!,       // ✅ key NO nullable
                    Weight = p.Weight,                // asumo decimal? o decimal
                    PackageId = p.Id
                })
                .ToListAsync();
            
            if (weightRules.Count > 0  && routeIds.Count > 0)
            {
              
                // ✅ filtra weights null y arma Dictionary<int, List<decimal>>
                weightsByRoute = packs
                    .Where(x => x.Weight.HasValue) // si Weight es decimal? (si es decimal, cambia esto)
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
                    DriverId = driverId,
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
                // borra ajustes anteriores (bonus/penalty/loan/etc)
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
                var routeRate = GetRateForRoute(route);
                var delivered = Math.Max(0, route.DeliveryStops - route.CNL);
                var failed    = Math.Max(0, route.CNL);

                  // var driverPerStop = rate.BaseAmount;
                // var routeRate = rates.FirstOrDefault(r =>
                // r.WarehouseId == route.WarehouseId);

                // routeRate ??= rates.FirstOrDefault(r =>
                //     r.WarehouseId == null);

                // if (routeRate == null)
                // {
                //     warnings.Add(
                //         $"Driver {driverId} has no rate configured for warehouse {route.WarehouseId}");
                //     continue;
                // }


                var driverPerStop =
                    (routeRate.RateType == "Mixed" || routeRate.RateType == "PerStop")
                        ? routeRate.BaseAmount
                        : 0m;

                decimal zonePerStop = 0m;

                if (route.Zone != null)
                    zonePerStop = route.Zone.PriceStop;

                decimal effectivePerStop = driverPerStop;
                string? stopTag;

                if (zonePerStop > 0 )
                {
                    if (isOnTrac)
                    {
                        effectivePerStop = Math.Max(driverPerStop, zonePerStop);
                       
                    }
                    else
                    {

                    }
                    stopTag = (driverPerStop > zonePerStop) ? "USE_DRIVER_BASE" : "USE_ZONE_RATE";
                }
                else
                {
                    effectivePerStop = driverPerStop;
                    stopTag = (route.Zone == null) ? "WARN_NO_ZONE" : "WARN_ZONE_PRICE_FALLBACK";
                }
                // ✅ 6.1) EXTRA POR PESO (por paquete)
                
                decimal routeSubtotal = 0m;
                var qtyExtraWeigth = 0m;

                if (delivered > 0)
                {
                    if (weightRules.Count > 0 && weightsByRoute.TryGetValue(route.Id, out var weightsForRoute))
                    {
                        var extraByRule = ComputeWeightExtras(weightsForRoute, weightRules);

                        foreach (var item in extraByRule)
                        {
                            // item: (rule, count, amountTotal)
                            var rule = item.Rule;
                            var qty = item.Count;
                            var rateExtra = Math.Max((rule.ExtraAmount + (routeRate.ExtraAmount ?? 0m))+ driverPerStop, zonePerStop) ;
                            qtyExtraWeigth += qty;

                            // Línea por regla (agregado) => qty * rateExtra = total
                            routeSubtotal += AddLine(
                                payRun,
                                "Earning",
                                route.Id.ToString(),
                                $"{route.Date:MMM dd, yyyy} - More than 1lb",
                                qty,
                                rateExtra ,
                                "WEIGHT_EXTRA",
                                route.Date,
                                route.Zone?.Id, route.Zone?.Area
                            );
                        }
                    }
                }
                 
                // ✅ PAYMENT TYPE (ENUM)
                switch (route.PaymentType)
                {
                    case PaymentType.PerRoute:
                        {
                            var priceRoute = route.PriceRoute;

                            if (priceRoute <= 0)
                            {
                                warnings.Add($"Ruta {route.Id}: PaymentType=PerRoute pero PriceRoute inválido ({priceRoute}); se pagó 0." );
                                AddLine(payRun, "Route", route.Id.ToString(),
                                    $"Route {route.Id} - {route.Date:yyyy-MM-dd} (PerRoute, sin precio)", 1m, 0m, "WARN_NO_ROUTE_PRICE",route.Date,route.Zone.Id, route.Zone.Area);
                            }
                            else
                            {
                                routeSubtotal += AddLine(payRun, "Route", route.Id.ToString(),
                                    $"Route {route.Id} - {route.Date:yyyy-MM-dd} (PerRoute)", 1m, (decimal)priceRoute, "PAY_PER_ROUTE",route.Date, route.Zone.Id, route.Zone.Area);
                            }

                            break;
                        }

                    case PaymentType.PerStop:
                        {
                            if (delivered > 0)
                            {

                                routeSubtotal += AddLine(
                                payRun,
                                "Earning",
                                route.Id.ToString(), 
                                $"{route.Date:MMM dd, yyyy}  {(route.Zone != null ? $"Zone {route.Zone.ZoneCode} " : "")} - Less than 1lb",
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

                    case PaymentType.Mixed:
                    default:
                        {
                            var priceRoute = route.PriceRoute;

                            if (priceRoute > 0)
                                routeSubtotal += AddLine(payRun, "Route", route.Id.ToString(),
                                    $"Route {route.Id} - {route.Date:yyyy-MM-dd} (Mixed-Route)", 1m, (decimal)priceRoute, "PAY_MIXED_ROUTE",route.Date, route.Zone?.Id, route.Zone?.Area);

                            if (delivered > 0)
                                routeSubtotal += AddLine(payRun, "Stop", route.Id.ToString(),
                                    "Delivered Stops (Mixed-Stop)", delivered, effectivePerStop, "PAY_MIXED_STOP",route.Date, route.Zone?.Id, route.Zone?.Area);

                            break;
                        }
                }

                // Penalidad CNL (si aplica)
                if (failed > 0 && routeRate.FailedStopPenalty.GetValueOrDefault() > 0)
                {
                    routeSubtotal += AddLine(payRun, "Stop", route.Id.ToString(),
                        "CNL Penalty", failed, -routeRate.FailedStopPenalty!.Value, "CNL_PENALTY",route.Date, route.Zone?.Id, route.Zone?.Area);
                }

                // Mínimo por ruta (si aplica)
                if (routeRate.MinPayPerRoute.HasValue && routeSubtotal < routeRate.MinPayPerRoute.Value)
                {
                    var diff = routeRate.MinPayPerRoute.Value - routeSubtotal;
                    routeSubtotal += AddLine(payRun, "Bonus", route.Id.ToString(),
                        "Minimum adjustment per route", 1m, diff, "MIN_ROUTE_ADJUST",route.Date, route.Zone?.Id, route.Zone?.Area);
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

            payRun.GrossAmount = gross;

            await ApplyLoanDeductionsAsync(payRun, userId);
            await _db.SaveChangesAsync();

        //2) recalcula Adjustments total (incluye loan deductions)
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
            // rules ya vienen ordenadas por Priority desc, MinWeight desc
            foreach (var r in rules)
            {
                if (weight < r.MinWeight) continue;
                if (r.MaxWeight.HasValue && weight > r.MaxWeight.Value) continue;
                return r;
            }
            return null;
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
