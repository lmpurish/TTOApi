using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FinanceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public FinanceController(ApplicationDbContext context)
        {
            _context = context;
        }

        private long GetUserId()
        {
            return long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }

        private long GetCompanyId()
        {
            var claim = User.FindFirst("CompanyId")?.Value
                        ?? User.FindFirst("companyId")?.Value;

            return long.Parse(claim ?? "0");
        }

        [HttpPost("company-revenue")]
        public async Task<IActionResult> UpsertCompanyRevenue(
            [FromBody] UpsertCompanyRevenueRequest request,
            CancellationToken ct)
        {
            var userId = GetUserId();
            var companyId = GetCompanyId();

            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId not found in token." });

            var payPeriod = await _context.PayPeriods
                .FirstOrDefaultAsync(x => x.Id == request.PayPeriodId &&
                                          x.CompanyId == companyId, ct);

            if (payPeriod == null)
                return NotFound(new { message = "Pay period not found." });

            var revenue = await _context.CompanyRevenues
                .FirstOrDefaultAsync(x =>
                    x.CompanyId == companyId &&
                    x.PayPeriodId == request.PayPeriodId &&
                    x.WarehouseId == request.WarehouseId &&
                    x.RevenueType == request.RevenueType, ct);

            if (revenue == null)
            {
                revenue = new CompanyRevenue
                {
                    CompanyId = (int)companyId,
                    PayPeriodId = request.PayPeriodId,
                    WarehouseId = request.WarehouseId,
                    RevenueType = request.RevenueType,
                    CreatedBy = (int)userId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.CompanyRevenues.Add(revenue);
            }
            else
            {
                revenue.UpdatedBy = (int?)userId;
                revenue.UpdatedAt = DateTime.UtcNow;
            }

            revenue.Revenue = request.Revenue;
            revenue.Expenses = request.Expenses;
            revenue.Adjustments = request.Adjustments;
            revenue.Notes = request.Notes;
            revenue.RevenueDate = request.RevenueDate ?? DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);

            return Ok(revenue);
        }

        [HttpGet("owner-dashboard")]
        public async Task<IActionResult> GetOwnerDashboard(
    [FromQuery] DateOnly startDate,
    [FromQuery] DateOnly endDate,
    [FromQuery] int? warehouseId,
    CancellationToken ct)
        {
            var companyId = GetCompanyId();

            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId not found in token." });

            if (endDate < startDate)
                return BadRequest(new { message = "End date cannot be before start date." });

            var payPeriodsQuery = _context.PayPeriods
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.StartDate >= startDate &&
                    x.EndDate <= endDate);

            if (warehouseId.HasValue)
            {
                payPeriodsQuery = payPeriodsQuery
                    .Where(x => x.WarehouseId == warehouseId.Value);
            }

            var payPeriods = await payPeriodsQuery.ToListAsync(ct);

            var payPeriodIds = payPeriods
                .Select(x => x.Id)
                .ToList();

            if (payPeriodIds.Count == 0)
            {
                return Ok(new
                {
                    startDate,
                    endDate,
                    totalRevenue = 0m,
                    totalExpenses = 0m,
                    totalPayroll = 0m,
                    netProfit = 0m,
                    margin = 0m,
                    totalPackages = 0,
                    avgPaidPerPackage = 0m,
                    profitPerPackage = 0m,
                    warehousePerformance = Array.Empty<object>(),
                    bestWarehouse = (object?)null,
                    worstWarehouse = (object?)null
                });
            }

            var revenuesQuery = _context.CompanyRevenues
                .AsNoTracking()
                .Include(x => x.Warehouse)
                .Where(x =>
                    x.CompanyId == companyId &&
                    payPeriodIds.Contains(x.PayPeriodId));

            var payrollQuery = _context.PayRuns
                .AsNoTracking()
                .Include(x => x.Driver)
                .Include(x => x.PayPeriod)
                .Where(x => payPeriodIds.Contains(x.PayPeriodId));

            var routesQuery = _context.Routes
                .AsNoTracking()
                .Include(x => x.Zone)
                .ThenInclude(x => x.Warehouse)
                .Where(x =>
                    x.Date.Date >= startDate.ToDateTime(TimeOnly.MinValue).Date &&
                    x.Date.Date <= endDate.ToDateTime(TimeOnly.MaxValue).Date &&
                    x.routeStatus == RouteStatus.Completed);

            if (warehouseId.HasValue)
            {
                revenuesQuery = revenuesQuery
                    .Where(x => x.WarehouseId == warehouseId.Value);

                payrollQuery = payrollQuery
                    .Where(x =>
                        x.PayPeriod.WarehouseId == warehouseId.Value ||
                        (x.Driver != null && x.Driver.WarehouseId == warehouseId.Value));

                routesQuery = routesQuery
                    .Where(x =>
                        x.WarehouseId == warehouseId.Value ||
                        (x.Zone != null && x.Zone.IdWarehouse == warehouseId.Value));
            }

            var revenues = await revenuesQuery.ToListAsync(ct);
            var payrolls = await payrollQuery.ToListAsync(ct);
            var routes = await routesQuery.ToListAsync(ct);

            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    (!warehouseId.HasValue || x.Id == warehouseId.Value))
                .Select(x => new
                {
                    x.Id,
                    Name = x.City + " - " + x.Company
                })
                .ToListAsync(ct);

            var performance = warehouses
                .Select(warehouse =>
                {
                    var warehousePeriodIds = payPeriods
                        .Where(x => x.WarehouseId == warehouse.Id)
                        .Select(x => x.Id)
                        .ToHashSet();

                    var revenue = revenues
                        .Where(x => x.WarehouseId == warehouse.Id)
                        .Sum(x => x.Revenue + x.Adjustments);

                    var expenses = revenues
                        .Where(x => x.WarehouseId == warehouse.Id)
                        .Sum(x => x.Expenses);

                    var payroll = payrolls
                        .Where(x =>
                            warehousePeriodIds.Contains(x.PayPeriodId) ||
                            (x.Driver != null && x.Driver.WarehouseId == warehouse.Id))
                        .Sum(x => x.NetAmount);

                    var packages = routes
                        .Where(x =>
                            x.WarehouseId == warehouse.Id ||
                            (x.Zone != null && x.Zone.IdWarehouse == warehouse.Id))
                        .Sum(x => x.Volumen);

                    var profit = revenue - payroll - expenses;

                    return new
                    {
                        warehouseId = warehouse.Id,
                        warehouse = warehouse.Name,
                        revenue,
                        expenses,
                        payroll,
                        profit,
                        margin = revenue > 0
                            ? profit / revenue * 100
                            : 0,
                        packages,
                        avgPaidPerPackage = packages > 0
                            ? payroll / packages
                            : 0,
                        profitPerPackage = packages > 0
                            ? profit / packages
                            : 0
                    };
                })
                .Where(x =>
                    x.revenue != 0 ||
                    x.payroll != 0 ||
                    x.expenses != 0 ||
                    x.packages != 0)
                .OrderByDescending(x => x.profit)
                .ToList();

            var totalRevenue = performance.Sum(x => x.revenue);
            var totalExpenses = performance.Sum(x => x.expenses);
            var totalPayroll = performance.Sum(x => x.payroll);
            var totalPackages = performance.Sum(x => x.packages);

            var netProfit = totalRevenue - totalPayroll - totalExpenses;

            var margin = totalRevenue > 0
                ? netProfit / totalRevenue * 100
                : 0;

            var avgPaidPerPackage = totalPackages > 0
                ? totalPayroll / totalPackages
                : 0;

            var profitPerPackage = totalPackages > 0
                ? netProfit / totalPackages
                : 0;

            var bestWarehouse = performance.FirstOrDefault();

            var worstWarehouse = performance
                .OrderBy(x => x.profit)
                .FirstOrDefault();

            return Ok(new
            {
                startDate,
                endDate,
                payPeriodCount = payPeriods.Count,
                totalRevenue,
                totalExpenses,
                totalPayroll,
                netProfit,
                margin,
                totalPackages,
                avgPaidPerPackage,
                profitPerPackage,
                warehousePerformance = performance,
                bestWarehouse,
                worstWarehouse
            });
        }
    }
}
