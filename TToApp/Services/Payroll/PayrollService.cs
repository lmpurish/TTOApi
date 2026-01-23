using Microsoft.EntityFrameworkCore;
using TToApp.Model;

namespace TToApp.Services.Payroll
{
    public class PayrollService
    {
        private readonly ApplicationDbContext _db;

        public PayrollService(ApplicationDbContext db) => _db = db;

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
            var rate = await _db.DriverRates
                .Where(r => r.DriverId == driverId
                            && r.EffectiveFrom <= weekEnd
                            && (r.EffectiveTo == null || r.EffectiveTo >= weekStart))
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync();

            if (rate is null)
                throw new InvalidOperationException("No hay DriverRate configurado para este driver y período.");

            // 3) PayrollConfig (por warehouse)
            PayrollConfig? payrollConfig = null;
            List<PayrollWeightRule> weightRules = new();
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
                    .FirstOrDefaultAsync(x => x.WarehouseId == (int)warehouseId.Value);

                if (payrollConfig?.EnableWeightExtra == true)
                {
                    weightRules = payrollConfig.WeightRules
                        .Where(r => r.IsActive)
                        .OrderByDescending(r => r.Priority)
                        .ThenByDescending(r => r.MinWeight)
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
             var packageIds = packs.Select(x => x.PackageId).Distinct().ToList();

            // 6.) Cheking is there are fines for the packages in the routes
            var finesByRoute = await (
                    from f in _db.Set<PayrollFine>().AsNoTracking()
                    join p in _db.Set<Packages>().AsNoTracking()
                        on f.PackageId equals p.Id
                    where packageIds.Contains(p.Id)
                        && f.Amount > 0  
                        && p.RoutesId != null
                    group f by (int)p.RoutesId into g
                    select new
                    {
                        RouteId = g.Key,
                        TotalFine = g.Sum(x => x.Amount)
                    }
                ).ToDictionaryAsync(x => x.RouteId, x => x.TotalFine);



            // 7) Crear / limpiar PayRun
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
                await _db.SaveChangesAsync();
            }

            decimal gross = 0m;
            var warnings = new List<string>();

            foreach (var route in routes)
            {
                var delivered = Math.Max(0, route.DeliveryStops - route.CNL);
                var failed = Math.Max(0, route.CNL);

                var driverPerStop = rate.BaseAmount;

                decimal zonePerStop = 0m;

                if (route.Zone != null)
                    zonePerStop = route.Zone.PriceStop;

                decimal effectivePerStop = driverPerStop;
                string? stopTag;

                if (zonePerStop > 0)
                {
                    effectivePerStop = Math.Max(driverPerStop, zonePerStop);
                    stopTag = (driverPerStop > zonePerStop) ? "USE_DRIVER_BASE" : "USE_ZONE_RATE";
                }
                else
                {
                    effectivePerStop = driverPerStop;
                    stopTag = (route.Zone == null) ? "WARN_NO_ZONE" : "WARN_ZONE_PRICE_FALLBACK";
                }

                decimal routeSubtotal = 0m;

                // ✅ PAYMENT TYPE (ENUM)
                switch (route.PaymentType)
                {
                    case PaymentType.PerRoute:
                        {
                            var priceRoute = route.PriceRoute;

                            if (priceRoute <= 0)
                            {
                                warnings.Add($"Ruta {route.Id}: PaymentType=PerRoute pero PriceRoute inválido ({priceRoute}); se pagó 0.");
                                AddLine(payRun, "Route", route.Id.ToString(),
                                    $"Ruta {route.Id} - {route.Date:yyyy-MM-dd} (PerRoute, sin precio)", 1m, 0m, "WARN_NO_ROUTE_PRICE");
                            }
                            else
                            {
                                routeSubtotal += AddLine(payRun, "Route", route.Id.ToString(),
                                    $"Ruta {route.Id} - {route.Date:yyyy-MM-dd} (PerRoute)", 1m, (decimal)priceRoute, "PAY_PER_ROUTE");
                            }

                            break;
                        }

                    case PaymentType.PerStop:
                        {
                            if (delivered > 0)
                            {
                                routeSubtotal += AddLine(payRun, "Stop", route.Id.ToString(),
                                    $"Stops entregados {(route.ZoneId == null ? "(sin zona)" : $"(zona {route.ZoneId})")} (PerStop)",
                                    delivered, effectivePerStop, stopTag);
                            }
                            else
                            {
                                AddLine(payRun, "Stop", route.Id.ToString(),
                                    "Stops entregados 0 (PerStop)", 0m, effectivePerStop, "INFO_ZERO_DELIVERED");
                            }

                            break;
                        }

                    case PaymentType.Mixed:
                    default:
                        {
                            var priceRoute = route.PriceRoute;

                            if (priceRoute > 0)
                                routeSubtotal += AddLine(payRun, "Route", route.Id.ToString(),
                                    $"Ruta {route.Id} - {route.Date:yyyy-MM-dd} (Mixed-Route)", 1m, (decimal)priceRoute, "PAY_MIXED_ROUTE");

                            if (delivered > 0)
                                routeSubtotal += AddLine(payRun, "Stop", route.Id.ToString(),
                                    "Stops entregados (Mixed-Stop)", delivered, effectivePerStop, "PAY_MIXED_STOP");

                            break;
                        }
                }


                // ✅ 6.1) EXTRA POR PESO (por paquete)
                if (weightRules.Count > 0 && weightsByRoute.TryGetValue(route.Id, out var weightsForRoute))
                {
                    var extraByRule = ComputeWeightExtras(weightsForRoute, weightRules);

                    foreach (var item in extraByRule)
                    {
                        // item: (rule, count, amountTotal)
                        var rule = item.Rule;
                        var qty = item.Count;
                        var rateExtra = rule.ExtraAmount;

                        // Línea por regla (agregado) => qty * rateExtra = total
                        routeSubtotal += AddLine(
                            payRun,
                            "WeightExtra",
                            route.Id.ToString(),
                            $"Extra por peso [{rule.MinWeight}-{(rule.MaxWeight.HasValue ? rule.MaxWeight.Value.ToString() : "∞")}]",
                            qty,
                            rateExtra,
                            "WEIGHT_EXTRA"
                        );
                    }
                }


                // Penalidad CNL (si aplica)
                if (failed > 0 && rate.FailedStopPenalty.GetValueOrDefault() > 0)
                {
                    routeSubtotal += AddLine(payRun, "Stop", route.Id.ToString(),
                        "Penalidad CNL", failed, -rate.FailedStopPenalty!.Value, "CNL_PENALTY");
                }

                // Mínimo por ruta (si aplica)
                if (rate.MinPayPerRoute.HasValue && routeSubtotal < rate.MinPayPerRoute.Value)
                {
                    var diff = rate.MinPayPerRoute.Value - routeSubtotal;
                    routeSubtotal += AddLine(payRun, "Bonus", route.Id.ToString(),
                        "Ajuste mínimo por ruta", 1m, diff, "MIN_ROUTE_ADJUST");
                }

                gross += routeSubtotal;
                
                // Penalidades por multas asociadas a la ruta 

                if (finesByRoute.TryGetValue(route.Id, out var totalFine))
                {
                    gross += AddLine(payRun, "Fine", route.Id.ToString(),
                        "Penalidades aplicadas", 1m, -totalFine, "FINE_APPLIED");
                }
            }

            if (warnings.Count > 0)
                AddLine(payRun, "Info", null, $"Warnings: {warnings.Count}", 0m, 0m, "WARN_SUMMARY");

            payRun.GrossAmount = gross;
            payRun.CalculatedAt = DateTime.UtcNow;
            payRun.CalculatedBy = userId;

            await _db.SaveChangesAsync();
            return payRun;
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
