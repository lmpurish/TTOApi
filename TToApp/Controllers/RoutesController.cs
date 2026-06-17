using ClosedXML.Excel;
using TToApp.Services.CommunicationRecipient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.Blazor;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Globalization;
using System.IO.Packaging;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Xml.Linq;
using TToApp.Constants;
using TToApp.DTOs;
using TToApp.Model;
using TToApp.Services.Audit;
using UglyToad.PdfPig;



namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoutesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly AuditService _auditService;
        private readonly ICommunicationRecipientService _communicationRecipients;

        public RoutesController(ApplicationDbContext context, EmailService emailService, INotificationService notificationService, AuditService auditService, ICommunicationRecipientService communicationRecipients)
        {
            _context = context;
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _notificationService = notificationService;
            _auditService = auditService;
            _communicationRecipients = communicationRecipients;
        }

        // GET: api/Routes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Routes>>> GetRoutes()
        {
            return await _context.Routes.ToListAsync();
        }
        [HttpGet("by-date")]
        public async Task<ActionResult<IEnumerable<object>>> GetRoutesByDate(
    [FromQuery] DateTime date,
    [FromQuery] int? warehouseId = null)
        {
            var userIdClaim = User.Claims
                .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

            var userRole = User.Claims
                .FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid user ID.");

            int resolvedWarehouseId;

            if (userRole == "Manager")
            {
                var managerWarehouseId = await _context.Users
                    .Where(u => u.Id == userId)
                    .Select(u => u.WarehouseId)
                    .FirstOrDefaultAsync();

                if (!managerWarehouseId.HasValue)
                    return NotFound("Manager not found or does not have an assigned warehouse.");

                resolvedWarehouseId = managerWarehouseId.Value;
            }
            else
            {
                if (!warehouseId.HasValue)
                    return BadRequest("Warehouse ID is required for non-manager users.");

                resolvedWarehouseId = warehouseId.Value;
            }

            var startDate = date.Date;
            var endDate = startDate.AddDays(1);

            var routesRaw = await _context.Routes
                .AsNoTracking()
                .Where(r =>
                    r.WarehouseId == resolvedWarehouseId ||
                    (r.Zone != null && r.Zone.IdWarehouse == resolvedWarehouseId) ||
                    (r.UserId != null && r.User.WarehouseId == resolvedWarehouseId)
                )
                .Where(r => r.Date >= startDate && r.Date < endDate)
                .Select(r => new
                {
                    r.Id,
                    r.Date,
                    r.DeliveryStops,
                    r.Volumen,
                    r.Los,
                    r.CustomerOnTime,
                    r.BranchOnTime,
                    r.CNL,
                    r.Attempts,
                    r.routeStatus,
                    r.PaymentType,
                    r.PriceRoute,
                    r.WarehouseId,

                    User = r.User == null ? null : new
                    {
                        r.User.Id,
                        r.User.IdentificationNumber,
                        r.User.Name,
                        r.User.LastName,
                        r.User.Email
                    },

                    Zone = r.Zone == null ? null : new
                    {
                        r.Zone.Id,
                        r.Zone.ZoneCode
                    }
                })
                .ToListAsync();

            var routes = routesRaw.Select(r => new
            {
                r.Id,
                r.Date,
                r.DeliveryStops,
                r.Volumen,
                r.Los,
                r.CustomerOnTime,
                r.BranchOnTime,
                r.CNL,
                r.Attempts,
                routeStatus = r.routeStatus != null
                    ? GetReadableStatus(r.routeStatus.Value)
                    : "no status",
                r.PaymentType,
                r.PriceRoute,
                r.WarehouseId,
                r.User,
                r.Zone,

                hasDriver = r.User != null,
                driverLabel = r.User == null
                    ? "Driver not found"
                    : $"{r.User.Name} {r.User.LastName}"
            });

            return Ok(routes);
        }


        [Authorize]
        [HttpPut("assign-routes")]
        public async Task<IActionResult> AssignRoutes([FromBody] List<RouteUpdateDto> routeUpdates)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out var currentUserId))
                return Unauthorized(new { message = "Invalid or missing user." });

            if (routeUpdates == null || routeUpdates.Count == 0)
                return BadRequest(new { message = "No routes provided." });

            var ids = routeUpdates.Select(x => x.Id).Distinct().ToList();

            var routes = await _context.Routes
                .Where(r => ids.Contains(r.Id))
                .ToListAsync();

            if (routes.Count == 0)
                return NotFound(new { message = "No matching routes found." });

            var updatesById = routeUpdates.ToDictionary(x => x.Id);
            var updated = new List<object>();
            var auditLogs = new List<(AuditLogDto Dto, object OldData, object NewData)>();

            foreach (var route in routes)
            {
                var u = updatesById[route.Id];
                bool changed = false;

                var oldData = new
                {
                    route.UserId,
                    route.ZoneId,
                    route.CNL,
                    route.routeStatus,
                    route.PaymentType,
                    route.PriceRoute
                };

                if (route.UserId != u.UserId)
                {
                    route.UserId = u.UserId;
                    changed = true;
                }

                var requestedStatus = ParseRouteStatus(u.RouteStatus);

                if (requestedStatus.HasValue)
                {
                    if (route.UserId.HasValue &&
                        requestedStatus is not (RouteStatus.Assigned or RouteStatus.InProgress or RouteStatus.Completed))
                    {
                        return BadRequest(new
                        {
                            message = "When a driver is assigned, status must be Assigned, InProgress, or Completed.",
                            routeId = route.Id,
                            requested = requestedStatus.Value.ToString()
                        });
                    }

                    if (!route.UserId.HasValue &&
                        requestedStatus is (RouteStatus.Assigned or RouteStatus.InProgress or RouteStatus.Completed))
                    {
                        return BadRequest(new
                        {
                            message = "Cannot set Assigned/InProgress/Completed without a driver.",
                            routeId = route.Id,
                            requested = requestedStatus.Value.ToString()
                        });
                    }

                    if (route.routeStatus != requestedStatus.Value)
                    {
                        route.routeStatus = requestedStatus.Value;
                        changed = true;
                    }
                }

                if (route.ZoneId != u.ZoneId)
                {
                    route.ZoneId = u.ZoneId;
                    changed = true;
                }

                if (route.CNL != u.CNL)
                {
                    route.CNL = (int)u.CNL;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(u.PaymentType))
                {
                    var normalizedPaymentType = u.PaymentType.Trim();

                    if (!Enum.TryParse<PaymentType>(normalizedPaymentType, out var paymentTypeEnum))
                    {
                        return BadRequest(new
                        {
                            message = "Invalid payment type. Allowed values are PerStop or PerRoute.",
                            routeId = route.Id,
                            paymentType = u.PaymentType
                        });
                    }

                    if (route.PaymentType != paymentTypeEnum)
                    {
                        route.PaymentType = paymentTypeEnum;
                        changed = true;
                    }

                    if (paymentTypeEnum == PaymentType.PerRoute)
                    {
                        if (u.PriceRoute == null || u.PriceRoute < 0)
                        {
                            return BadRequest(new
                            {
                                message = "PriceRoute is required when PaymentType is PerRoute.",
                                routeId = route.Id
                            });
                        }

                        var priceRoute = Convert.ToDouble(u.PriceRoute.Value);

                        if (route.PriceRoute != priceRoute)
                        {
                            route.PriceRoute = priceRoute;
                            changed = true;
                        }
                    }
                    else
                    {
                        if (route.PriceRoute != 0)
                        {
                            route.PriceRoute = 0;
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    var newData = new
                    {
                        route.UserId,
                        route.ZoneId,
                        route.CNL,
                        route.routeStatus,
                        route.PaymentType,
                        route.PriceRoute
                    };

                    updated.Add(new
                    {
                        route.Id,
                        route.ZoneId,
                        route.CNL,
                        route.UserId,
                        routeStatus = route.routeStatus.ToString(),
                        paymentType = route.PaymentType,
                        priceRoute = route.PriceRoute
                    });

                    auditLogs.Add((
                        new AuditLogDto
                        {
                            UserId = currentUserId,
                            Action = AuditLogAction.RouteUpdated,
                            Entity = "Route",
                            EntityId = route.Id.ToString(),
                            Description = $"Route {route.Id} updated"
                        },
                        oldData,
                        newData
                    ));
                }
            }

            await _context.SaveChangesAsync();

            foreach (var log in auditLogs)
            {
                await _auditService.LogChangeAsync(log.Dto, log.OldData, log.NewData);
            }

            return Ok(new
            {
                message = "Routes updated successfully.",
                count = updated.Count,
                updatedRoutes = updated
            });
        }


        [Authorize]
        [HttpPost("{id:int}/claim")]
        public async Task<IActionResult> ClaimRoute(int id)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized(new { message = "Invalid or missing user." });

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var oldData = await _context.Routes
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new
                {
                    r.Id,
                    r.UserId,
                    r.routeStatus,
                    r.WarehouseId,
                    r.ZoneId,
                    r.Date
                })
                .FirstOrDefaultAsync();

            var rows = await _context.Routes
                .Where(r => r.Id == id
                    && r.UserId == null
                    && (r.routeStatus == RouteStatus.Available || r.routeStatus == RouteStatus.Future))
                .ExecuteUpdateAsync(upd => upd
                    .SetProperty(r => r.UserId, userId)
                    .SetProperty(r => r.routeStatus, RouteStatus.Assigned));

            if (rows == 1)
            {
                await tx.CommitAsync();

                var route = await _context.Routes
                    .AsNoTracking()
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == id);

                await _auditService.LogChangeAsync(
                    new AuditLogDto
                    {
                        UserId = userId,
                        Action = AuditLogAction.RouteAssigned,
                        Entity = "Route",
                        EntityId = id.ToString(),
                        Description = $"Route {id} claimed by driver {userId}",
                        WarehouseId = route?.WarehouseId
                    },
                    oldData,
                    new
                    {
                        route?.Id,
                        route?.UserId,
                        route?.routeStatus,
                        route?.WarehouseId,
                        route?.ZoneId,
                        route?.Date
                    }
                );

                return Ok(new
                {
                    message = "Route successfully claimed.",
                    route = new { route?.Id, route?.routeStatus, route?.UserId }
                });
            }

            await tx.RollbackAsync();

            var exists = await _context.Routes.AnyAsync(r => r.Id == id);
            if (!exists)
                return NotFound(new { message = "Route not found." });

            return Conflict(new { message = "Route has already been claimed or is not available." });
        }

        [Authorize]
        [HttpPost("{id:int}/removeAssigned")]
        public async Task<IActionResult> RemoveRoute(int id)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized(new { message = "Invalid or missing user." });

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var oldData = await _context.Routes
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new
                {
                    r.Id,
                    r.UserId,
                    r.routeStatus,
                    r.WarehouseId,
                    r.ZoneId,
                    r.Date
                })
                .FirstOrDefaultAsync();

            var rows = await _context.Routes
                .Where(r => r.Id == id
                            && r.UserId == userId
                            && r.routeStatus == RouteStatus.Assigned)
                .ExecuteUpdateAsync(upd => upd
                    .SetProperty(r => r.UserId, (int?)null)
                    .SetProperty(r => r.routeStatus, RouteStatus.Available));

            if (rows == 1)
            {
                await tx.CommitAsync();

                var route = await _context.Routes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == id);

                await _auditService.LogChangeAsync(
                    new AuditLogDto
                    {
                        UserId = userId,
                        Action = AuditLogAction.RouteUpdated,
                        Entity = "Route",
                        EntityId = id.ToString(),
                        Description = $"Route {id} unassigned by driver {userId}",
                        WarehouseId = route?.WarehouseId
                    },
                    oldData,
                    new
                    {
                        route?.Id,
                        route?.UserId,
                        route?.routeStatus,
                        route?.WarehouseId,
                        route?.ZoneId,
                        route?.Date
                    }
                );

                return Ok(new
                {
                    message = "Route was unassigned successfully.",
                    route = new { route?.Id, route?.routeStatus, route?.UserId }
                });
            }

            await tx.RollbackAsync();

            var info = await _context.Routes
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new { r.UserId, r.routeStatus })
                .FirstOrDefaultAsync();

            if (info is null)
                return NotFound(new { message = "Route not found." });

            if (info.UserId is null)
                return Conflict(new { message = "Route is not currently assigned." });

            if (info.UserId != userId)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You cannot remove a route assigned to another user." });

            if (info.routeStatus != RouteStatus.Assigned)
                return Conflict(new { message = $"Route can only be removed when status is Assigned (current: {info.routeStatus})." });

            return Conflict(new { message = "Could not unassign the route." });
        }


        // GET: api/Routes/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Routes>> GetRoute(int id)
        {
            var route = await _context.Routes.FindAsync(id);

            if (route == null)
            {
                return NotFound();
            }

            return route;
        }

        // PUT: api/Routes/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutRoute(int id, Routes route)
        {
            if (id != route.Id)
            {
                return BadRequest();
            }

            _context.Entry(route).State = Microsoft.EntityFrameworkCore.EntityState.Modified; ;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!RouteExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/Routes
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754


        // DELETE: api/Routes/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRoute(int id)
        {
            var route = await _context.Routes.FindAsync(id);
            if (route == null)
            {
                return NotFound();
            }

            _context.Routes.Remove(route);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool RouteExists(int id)
        {
            return _context.Routes.Any(e => e.Id == id);
        }

        [Authorize]
        [HttpPost("upload/{warehouseId}")]
        public async Task<IActionResult> UploadXmlFile(IFormFile file, int warehouseId)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdStr, out var currentUserId);
            if (file == null || file.Length == 0 || Path.GetExtension(file.FileName).ToLower() != ".xml")
                return BadRequest(new { message = "Debe subir un archivo XML válido con extensión .xml." });

            try
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                stream.Position = 0;

                XDocument xmlDoc = XDocument.Load(stream);
                XNamespace ns = xmlDoc.Root?.GetDefaultNamespace() ?? "";

                var reportDateAttr = xmlDoc.Root?.Attribute("SummaryHeader_TextBox")?.Value;
                if (string.IsNullOrEmpty(reportDateAttr))
                    return BadRequest(new { message = "No se encontró la fecha del reporte en el XML." });

                var reportDateStr = reportDateAttr.Replace("Report Date: ", "").Trim();

                if (!DateTime.TryParse(reportDateStr, out DateTime reportDate))
                    return BadRequest(new { message = $"La fecha del reporte no es válida: '{reportDateStr}'." });

                var details = xmlDoc.Descendants(ns + "Detail");

                var losBeforeCutoffDetails = xmlDoc
                    .Descendants(ns + "LOSBeforeCutoff_Tablix")
                    .Descendants(ns + "Details4")
                    .ToList();

                var Cnls = xmlDoc
                    .Descendants(ns + "CNL_Tablix")
                    .Descendants(ns + "Details5")
                    .ToList();

                var IncompleteDay2 = xmlDoc
                    .Descendants(ns + "IncompleteDay2_Tablix")
                    .Descendants(ns + "Details3")
                    .ToList();

                // RSP metrics desde XML
                var branchOnTimeForRSPElement = xmlDoc.Descendants(ns + "PerformanceIndex2")
                    .FirstOrDefault(p => (string)p.Attribute("PerformanceIndex2") == "Branch On Time %")
                    ?.Element(ns + "Textbox218")?.Attribute("Textbox232")?.Value;

                var LosForRSPElement = xmlDoc.Descendants(ns + "PerformanceIndex2")
                    .FirstOrDefault(p => (string)p.Attribute("PerformanceIndex2") == "Los %")
                    ?.Element(ns + "Textbox218")?.Attribute("Textbox232")?.Value;

                double branchOnTimeForRSP = !string.IsNullOrEmpty(branchOnTimeForRSPElement)
                    ? SafeParseDouble(branchOnTimeForRSPElement) * 100
                    : 0;

                double LosForRSP = !string.IsNullOrEmpty(LosForRSPElement)
                    ? SafeParseDouble(LosForRSPElement) * 100
                    : 0;

                var notifiedPackages = new List<(string Tracking, string Status, int DaysElapsed)>();

                var spValues = details
                    .Select(d => d.Attribute("SP__")?.Value?.Trim())
                    .Where(sp => !string.IsNullOrEmpty(sp))
                    .Distinct()
                    .ToList();

                if (!spValues.Any())
                    return BadRequest(new { message = "No se encontraron IdentificationNumber en el XML." });

                var users = await _context.Users
                    .Where(u => spValues.Contains(u.IdentificationNumber) && u.WarehouseId == warehouseId)
                    .ToListAsync();

                var notFoundInUsers = spValues
                    .Except(users.Select(u => u.IdentificationNumber))
                    .ToList();

                var rsp = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.UserRole == global::User.Role.Rsp &&
                        u.WarehouseId == warehouseId);

                if (rsp == null)
                {
                    return BadRequest(new { message = "No se encontró RSP para este Warehouse." });
                }

                var manager = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.WarehouseId == warehouseId &&
                        u.UserRole == global::User.Role.Manager);

                var userIds = users
                    .GroupBy(u => new { u.IdentificationNumber, u.WarehouseId })
                    .ToDictionary(g => (g.Key.IdentificationNumber, g.Key.WarehouseId), g => g.First().Id);


                // IMPORTANTE: filtrar por warehouse
                var existingRouteKeys = await _context.Routes
                     .Where(r => r.Date.Date == reportDate.Date && r.WarehouseId == warehouseId)
                     .Select(r => new
                     {
                         r.UserId,
                         r.DriverIdentificationNumber
                     })
                     .ToListAsync();

                var routesToSave = new List<Routes>();
                var Packages = new List<Packages>();

                foreach (var detail in details)
                {
                    string spValue = detail.Attribute("SP__")?.Value?.Trim() ?? "0";

                    int volume3 = SafeParseInt(detail.Attribute("Volume3")?.Value);
                    int deliveryPieces = SafeParseInt(detail.Attribute("Delivery_Pieces3")?.Value);
                    int attempts = SafeParseInt(detail.Attribute("Incomplete_D5")?.Value);
                    int volumen = volume3 > 0 ? volume3 : deliveryPieces;
                    int? userId = null;

                    if (userIds.TryGetValue((spValue, warehouseId), out int foundUserId))
                    {
                        userId = foundUserId;
                    }

                    bool routeAlreadyExists = existingRouteKeys.Any(r =>
                        (userId != null && r.UserId == userId) ||
                        (!string.IsNullOrWhiteSpace(r.DriverIdentificationNumber) &&
                         r.DriverIdentificationNumber == spValue)
                    );

                    if (routeAlreadyExists)
                        continue;

                    double los = SafeParseDouble(detail.Attribute("LOS3")?.Value) * 100;

                    int cnlValue = SafeParseInt(detail.Attribute("CNL3")?.Value);

                    int customerOnTimeNumerator = volumen > 0
                        ? SafeParseInt(detail.Attribute("Customer_On_Time_Numerator")?.Value)
                        : 0;

                    int customerOnTimeDenominator = volumen > 0
                        ? SafeParseInt(detail.Attribute("Customer_On_Time_Denominator")?.Value)
                        : 1;

                    double customerOnTime = customerOnTimeDenominator > 0
                        ? (double)customerOnTimeNumerator / customerOnTimeDenominator * 100
                        : 0;

                    var route = new Routes
                    {
                        Date = reportDate,
                        DeliveryStops = volumen > 0
                        ? SafeParseInt(detail.Attribute("Delivery_Stops3")?.Value)
                        : 0,

                        Volumen = volumen,
                        Los = los,
                        CustomerOnTime = customerOnTime,

                        UserId = userId,
                        DriverIdentificationNumber = spValue,

                        routeStatus = RouteStatus.Completed,
                        Attempts = attempts,
                        CNL = cnlValue,
                        BranchOnTime = 100,
                        WarehouseId = warehouseId
                    };

                    routesToSave.Add(route);
                }

                // FIX: crear o actualizar ruta del RSP
                double rspVolume = GetScorecardValue(xmlDoc, ns, "Volume");
                double rspDeliveryStops = GetScorecardValue(xmlDoc, ns, "Delivery Stops");
                double rspLos = GetScorecardValue(xmlDoc, ns, "Los %");
                double rspCustomerOnTime = GetScorecardValue(xmlDoc, ns, "Customer On Time %");
                double rspBranchOnTime = GetScorecardValue(xmlDoc, ns, "Branch On Time %");

                var existingRspRoute = await _context.Routes
                    .FirstOrDefaultAsync(r =>
                        r.Date.Date == reportDate.Date &&
                        r.UserId == rsp.Id &&
                        r.WarehouseId == warehouseId);

                if (existingRspRoute == null)
                {
                    routesToSave.Add(new Routes
                    {
                        Date = reportDate,
                        UserId = rsp.Id,
                        WarehouseId = warehouseId,
                        routeStatus = RouteStatus.Completed,

                        //    Volumen = (int)rspVolume,
                        //    DeliveryStops = (int)rspDeliveryStops,
                        Los = rspLos,
                        CustomerOnTime = rspCustomerOnTime,

                        BranchOnTime = rspBranchOnTime,

                        Attempts = 0,
                        CNL = 0
                    });
                }
                else
                {
                    //   existingRspRoute.Volumen = (int)rspVolume;
                    //   existingRspRoute.DeliveryStops = (int)rspDeliveryStops;
                    existingRspRoute.Los = rspLos;
                    existingRspRoute.CustomerOnTime = rspCustomerOnTime;

                    existingRspRoute.BranchOnTime = rspBranchOnTime;

                    _context.Routes.Update(existingRspRoute);
                }

                if (routesToSave.Any())
                {
                    _context.Routes.AddRange(routesToSave);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    await _context.SaveChangesAsync();
                }

                if (losBeforeCutoffDetails.Count > 0)
                {
                    foreach (var detail in losBeforeCutoffDetails)
                    {
                        string tracking = detail.Attribute("tracking4")?.Value?.Trim();
                        string address = detail.Attribute("Delivery_Address4")?.Value?.Trim();
                        string city = detail.Attribute("Delviery_City4")?.Value?.Trim();
                        string state = detail.Attribute("Delivery_State4")?.Value?.Trim();
                        string zip = detail.Attribute("Delivery_Zip4")?.Value?.Trim();

                        int rsp1 = int.TryParse(rsp.IdentificationNumber, out var result) ? result : 0;

                        if (string.IsNullOrWhiteSpace(tracking))
                            continue;

                        var existingPackage = await _context.Packages
                            .FirstOrDefaultAsync(p => p.Tracking == tracking);

                        if (existingPackage != null)
                        {
                            if (existingPackage.Status == PackageStatus.RD)
                            {
                                existingPackage.DaysElapsed += 1;
                                existingPackage.IncidentDate = reportDate;
                                _context.Packages.Update(existingPackage);
                                await _context.SaveChangesAsync();
                            }

                            continue;
                        }

                        Packages.Add(new Packages
                        {
                            RSP = rsp1,
                            Tracking = tracking,
                            Address = address,
                            City = city,
                            State = state,
                            ZipCode = zip,
                            IncidentDate = reportDate,
                            Status = PackageStatus.RD,
                            DaysElapsed = 0
                        });
                    }
                }

                if (Cnls.Count > 0)
                {
                    var identificationToUserId = users.ToDictionary(u => u.IdentificationNumber, u => u.Id);

                    var routeDictionary = await _context.Routes
                        .Where(r => r.Date.Date == reportDate.Date && r.WarehouseId == warehouseId)
                        .ToDictionaryAsync(r => r.UserId, r => r.Id);

                    var existingTrackings = await _context.Packages
                        .Where(p => p.Status == PackageStatus.CNL)
                        .Select(p => p.Tracking)
                        .ToListAsync();

                    foreach (var detail in Cnls)
                    {
                        int rsp1 = int.TryParse(rsp.IdentificationNumber, out var result) ? result : 0;

                        string tracking = detail.Attribute("tracking5")?.Value?.Trim();
                        string driverIdentification = detail.Attribute("Driver5")?.Value?.Trim();
                        string address = detail.Attribute("Delivery_Address5")?.Value?.Trim();
                        string city = detail.Attribute("Delviery_City5")?.Value?.Trim();
                        string state = detail.Attribute("Delivery_State5")?.Value?.Trim();
                        string zip = detail.Attribute("Delivery_Zip5")?.Value?.Trim();
                        string distance = detail.Attribute("Distance")?.Value?.Trim();
                        string scanLat = detail.Attribute("Scan_Lat")?.Value?.Trim();
                        string scanLon = detail.Attribute("Scan_Long")?.Value?.Trim();
                        string addrLat = detail.Attribute("Addr_Lat")?.Value?.Trim();
                        string addrLon = detail.Attribute("Addr_Long")?.Value?.Trim();

                        if (string.IsNullOrWhiteSpace(tracking) || string.IsNullOrWhiteSpace(driverIdentification))
                            continue;

                        if (!identificationToUserId.TryGetValue(driverIdentification, out int userId))
                            continue;

                        if (!routeDictionary.TryGetValue(userId, out int routeId))
                            continue;

                        if (existingTrackings.Contains(tracking))
                            continue;

                        Packages.Add(new Packages
                        {
                            Tracking = tracking,
                            Address = address,
                            City = city,
                            State = state,
                            ZipCode = zip,
                            Distance = distance,
                            ScanLat = scanLat,
                            ScanLon = scanLon,
                            AddrLat = addrLat,
                            AddrLon = addrLon,
                            IncidentDate = reportDate,
                            Status = PackageStatus.CNL,
                            RoutesId = routeId,
                            DaysElapsed = 0,
                            RSP = rsp1
                        });
                    }
                }

                if (IncompleteDay2.Count > 0)
                {
                    var existingTrackings = await _context.Packages
                        .Where(p => p.Tracking != null)
                        .Select(p => p.Tracking.Trim().ToUpper())
                        .ToListAsync();

                    foreach (var detail in IncompleteDay2)
                    {
                        int rsp1 = int.TryParse(rsp.IdentificationNumber, out var result) ? result : 0;

                        string tracking = detail.Attribute("tracking3")?.Value?.Trim();
                        string address = detail.Attribute("Delivery_Address3")?.Value?.Trim();
                        string city = detail.Attribute("Delviery_City3")?.Value?.Trim();
                        string state = detail.Attribute("Delivery_State3")?.Value?.Trim();
                        string zip = detail.Attribute("Delivery_Zip3")?.Value?.Trim();
                        string CurrentStatuscode1 = detail.Attribute("CurrentStatuscode1")?.Value?.Trim();

                        if (string.IsNullOrWhiteSpace(tracking))
                            continue;

                        var normalizedTracking = tracking.Trim().ToUpper();

                        if (existingTrackings.Contains(normalizedTracking))
                        {
                            if (new[] { "CO", "NH", "OD", "WA", "ED", "UG", "HW" }.Contains(CurrentStatuscode1))
                            {
                                var existingPackage = await _context.Packages
                                    .FirstOrDefaultAsync(p => p.Tracking.Trim().ToUpper() == normalizedTracking);

                                if (existingPackage != null && Enum.TryParse<PackageStatus>(CurrentStatuscode1, out var parsedStatus1))
                                {
                                    existingPackage.Status = parsedStatus1;
                                    existingPackage.DaysElapsed += 1;
                                    existingPackage.IncidentDate = reportDate;

                                    if (manager != null)
                                    {
                                        await _notificationService.NotifyAsync(
                                            userId: manager.Id,
                                            title: "📦 Overdue Package Alert",
                                            message: $"The package with tracking number {existingPackage.Tracking} has been open for more than 1 day. Please follow up.",
                                            type: NotificationType.Success,
                                            url: "",
                                            source: "Tracking System"
                                        );
                                    }

                                    notifiedPackages.Add((
                                        existingPackage.Tracking,
                                        existingPackage.Status.ToString(),
                                        existingPackage.DaysElapsed
                                    ));
                                }
                            }

                            continue;
                        }

                        if (Enum.TryParse<PackageStatus>(CurrentStatuscode1, out var parsedStatus))
                        {
                            Packages.Add(new Packages
                            {
                                Tracking = tracking,
                                Address = address,
                                City = city,
                                State = state,
                                ZipCode = zip,
                                IncidentDate = reportDate,
                                Status = parsedStatus,
                                DaysElapsed = 1,
                                RSP = rsp1
                            });
                        }
                    }
                }

                if (Packages.Count > 0)
                {
                    _context.Packages.AddRange(Packages);
                    await _context.SaveChangesAsync();
                }

                if (manager != null)
                {
                    var adminEmails = _context.Users
                        .Where(u => u.UserRole.Value == global::User.Role.Admin && !string.IsNullOrEmpty(u.Email))
                        .Select(u => u.Email)
                        .ToList();

                    var warehouse = GetWarehouseCity(warehouseId);

                    var tableHtml = new StringBuilder();
                    tableHtml.AppendLine("<table style='width:100%; border-collapse:collapse;'>");
                    tableHtml.AppendLine("<thead><tr style='background-color:#f2f2f2;'>");
                    tableHtml.AppendLine("<th style='border:1px solid #ddd; padding:8px;'>Tracking</th>");
                    tableHtml.AppendLine("<th style='border:1px solid #ddd; padding:8px;'>Status</th>");
                    tableHtml.AppendLine("<th style='border:1px solid #ddd; padding:8px;'>Days Elapsed</th>");
                    tableHtml.AppendLine("</tr></thead>");
                    tableHtml.AppendLine("<tbody>");

                    foreach (var pkg in notifiedPackages)
                    {
                        tableHtml.AppendLine("<tr>");
                        tableHtml.AppendLine($"<td style='border:1px solid #ddd; padding:8px;'>{pkg.Tracking}</td>");
                        tableHtml.AppendLine($"<td style='border:1px solid #ddd; padding:8px;'>{pkg.Status}</td>");
                        tableHtml.AppendLine($"<td style='border:1px solid #ddd; padding:8px;'>{pkg.DaysElapsed}</td>");
                        tableHtml.AppendLine("</tr>");
                    }

                    tableHtml.AppendLine("</tbody></table>");

                    var placeholders = new Dictionary<string, string>
            {
                { "warehouse", warehouse },
                { "date", reportDate.ToString("MMMM dd, yyyy", new System.Globalization.CultureInfo("en-US")) },
                { "packageList", tableHtml.ToString() }
            };

                    await _emailService.SendEmailAsync(
                        toEmail: manager.Email,
                        subject: "Information Loaded!",
                        "ConfirmUploadXml.cshtml",
                        placeholders: placeholders,
                        copy: false
                    );

                    foreach (var email in adminEmails)
                    {
                        await _emailService.SendEmailAsync(
                            toEmail: email,
                            subject: "Information Loaded!",
                            "ConfirmUploadXml.cshtml",
                            placeholders: placeholders,
                            copy: false
                        );
                    }
                }

                await _auditService.LogAsync(new AuditLogDto
                {
                    UserId = currentUserId,
                    Action = AuditLogAction.XmlImport,
                    Entity = "Routes",

                    Description =
                        $"XML imported successfully. Warehouse={warehouseId}, " +
                        $"RoutesCreated={routesToSave.Count}, " +
                        $"PackagesCreated={Packages.Count}, " +
                        $"DriversNotFound={notFoundInUsers.Count}",

                    NewValue = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        FileName = file.FileName,
                        WarehouseId = warehouseId,
                        ReportDate = reportDate,

                        RoutesCreated = routesToSave.Count,
                        PackagesCreated = Packages.Count,

                        DriversNotFound = notFoundInUsers,

                        Rsp = new
                        {
                            rsp.Id,
                            rsp.IdentificationNumber,
                            BranchOnTime = branchOnTimeForRSP,
                            Los = LosForRSP
                        }
                    })
                });
                return Ok(new
                {
                    message = $"{routesToSave.Count} registros guardados/actualizados en Routes, incluyendo el RSP.",
                    rsp = new
                    {
                        rsp.Id,
                        rsp.IdentificationNumber,
                        BranchOnTime = branchOnTimeForRSP,
                        Los = LosForRSP
                    },
                    notFoundUsers = notFoundInUsers
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error al procesar el XML",
                    error = ex.Message,
                    innerException = ex.InnerException?.Message
                });
            }
        }
        private double GetScorecardValue(XDocument xmlDoc, XNamespace ns, string metricName)
        {
            var value = xmlDoc.Descendants(ns + "PerformanceIndex2")
                .FirstOrDefault(p =>
                    string.Equals(
                        (string)p.Attribute("PerformanceIndex2"),
                        metricName,
                        StringComparison.OrdinalIgnoreCase))
                ?.Element(ns + "Textbox218")
                ?.Attribute("Textbox232")
                ?.Value;

            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var result = SafeParseDouble(value);

            if (metricName.Contains("%") && result <= 1)
                result *= 100;

            return Math.Round(result, 2);
        }

        [HttpGet("routes-by-date-and-warehouse")]
        public async Task<ActionResult<List<RouteUserZoneDto>>> GetRoutesByDateAndWarehouseAsync([FromQuery] DateTime date, [FromQuery] int warehouseId)
        {
            var results = await _context.Routes
                .Where(r => r.Date.Date == date.Date && r.User.WarehouseId == warehouseId)
                .Include(r => r.User)
                .Include(r => r.Zone)
                .Select(r => new RouteUserZoneDto
                {
                    Id = r.Id,
                    IdentificationNumber = r.User.IdentificationNumber,
                    UserName = r.User.Name,
                    UserLastName = r.User.LastName,
                    Zone = r.Zone != null ? r.Zone.ZoneCode : "Sin zona"
                })
                .ToListAsync();

            return Ok(results);
        }
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PostRoutes(RoutesDto routesDto)
        {
            if (routesDto == null)
                return BadRequest("Datos inválidos.");

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdStr, out var userId);

            try
            {
                var routes = new Routes
                {
                    Date = routesDto.Date,
                    Volumen = routesDto.Volumen,
                    DeliveryStops = (int)routesDto.DeliveryStops,
                    ZoneId = routesDto.ZoneId,
                    routeStatus = RouteStatus.Created,
                    PriceRoute = routesDto.PriceRoute,
                    PaymentType = routesDto.paymentType,
                    WarehouseId = routesDto.WarehouseId
                };

                _context.Routes.Add(routes);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(new AuditLogDto
                {
                    UserId = userId,
                    Action = AuditLogAction.RouteCreated,
                    Entity = "Route",
                    EntityId = routes.Id.ToString(),
                    Description = $"Route {routes.Id} created manually",
                    NewValue = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        routes.Id,
                        routes.Date,
                        routes.Volumen,
                        routes.DeliveryStops,
                        routes.ZoneId,
                        routes.routeStatus,
                        routes.PriceRoute,
                        routes.PaymentType,
                        routes.WarehouseId
                    })
                });

                return Ok(new { message = "Route added successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al guardar la ruta: {ex}");
                return StatusCode(500, "Error interno del servidor.");
            }
        }
        [Authorize]
        [HttpPost("{id:int}/{actionSegment}")]
        public async Task<IActionResult> ChangeStatus(int id, string actionSegment)
        {
            var userId = User.GetUserId();
            if (userId is null)
                return Unauthorized(new { message = "Missing user id claim in token." });

            var route = await _context.Set<Routes>().FirstOrDefaultAsync(r => r.Id == id);
            if (route is null)
                return NotFound(new { message = $"Route {id} not found." });

            var isPrivileged = User.HasAnyRole("Admin", "Manager");
            if (route.UserId.HasValue)
            {
                if (route.UserId.Value != userId && !isPrivileged)
                    return Forbid();
            }
            else if (!isPrivileged)
            {
                return Conflict(new { message = "Route has no assigned owner. Cannot change status." });
            }

            var current = route.routeStatus ?? RouteStatus.Pending;
            var action = (actionSegment ?? "").Trim().ToLowerInvariant().Replace("_", "-");

            // Same-day check for start-loading (America/Chicago)
            if (IsStartLoading(action))
            {
                var todayCentral = GetTodayCentral();
                if (route.Date.Date != todayCentral)
                {
                    return Conflict(new
                    {
                        message = "You can only start loading on the same day as the route.",
                        scheduledDate = route.Date.ToString("yyyy-MM-dd"),
                        today = todayCentral.ToString("yyyy-MM-dd")
                    });
                }
            }

            if (!TryResolveTransition(current, action, out var next, out var error))
            {
                return Conflict(new
                {
                    message = error,
                    currentStatus = current.ToString()
                });
            }

            if (current == next)
            {
                return Ok(new
                {
                    id = route.Id,
                    previousStatus = current.ToString(),
                    newStatus = next.ToString(),
                    message = "No state change (idempotent)."
                });
            }

            route.routeStatus = next;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Concurrency conflict while updating the route." });
            }

            return Ok(new
            {
                id = route.Id,
                previousStatus = current.ToString(),
                newStatus = next.ToString()
            });
        }
        private static bool IsStartLoading(string action)
          => action == "start-loading" || action == "startloading";


        private static System.DateTime GetTodayCentral()
        {
            var ids = new[] { "Central Standard Time", "America/Chicago" };
            System.TimeZoneInfo? tz = null;
            foreach (var id in ids)
            {
                try { tz = System.TimeZoneInfo.FindSystemTimeZoneById(id); break; }
                catch { }
            }
            var now = tz is not null
                ? System.TimeZoneInfo.ConvertTime(System.DateTime.UtcNow, tz)
                : System.DateTime.Now;
            return now.Date;
        }

        // ===== Transiciones permitidas =====
        private static bool TryResolveTransition(RouteStatus current, string action, out RouteStatus next, out string error)
        {
            error = string.Empty;
            next = current;

            switch (action)
            {
                case "start-loading":
                case "startloading":
                    if (current is RouteStatus.Pending or RouteStatus.Assigned or RouteStatus.Available or RouteStatus.Future or RouteStatus.Created)
                    {
                        next = RouteStatus.Loading;
                        return true;
                    }
                    error = $"Cannot move from {current} to Loading using '{action}'. Allowed from: Pending/Assigned/Available/Future/Created.";
                    return false;

                case "start":
                    if (current == RouteStatus.Loading)
                    {
                        next = RouteStatus.InProgress;
                        return true;
                    }
                    error = $"Cannot move from {current} to InProgress using '{action}'. Allowed from: Loading.";
                    return false;

                case "request-complete":
                case "requestcomplete":
                    if (current == RouteStatus.InProgress)
                    {
                        next = RouteStatus.PendingCompletion;
                        return true;
                    }
                    error = $"Cannot request completion from {current}. Allowed from: InProgress.";
                    return false;

                case "complete":
                    if (current is RouteStatus.InProgress or RouteStatus.PendingCompletion)
                    {
                        next = RouteStatus.Completed;
                        return true;
                    }
                    error = $"Cannot move from {current} to Completed using '{action}'. Allowed from: InProgress/PendingCompletion.";
                    return false;

                case "cancel":
                case "cancelled":
                case "canceled":
                    if (current is RouteStatus.Completed or RouteStatus.Cancelled)
                    {
                        error = $"Cannot cancel a route in {current} state.";
                        return false;
                    }
                    next = RouteStatus.Cancelled;
                    return true;

                default:
                    error = "Unknown action '{action}'. Allowed: start-loading, start, request-complete, complete, cancel.";
                    return false;
            }
        }


        // ===== Helpers de Claims =====

        [Authorize]
        [HttpGet("available-routes")]
        public async Task<ActionResult<IEnumerable<object>>> GetAvailableRoutes()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid user ID.");

            // Traemos Warehouse para saber ciudad y warehouse asignado
            var user = await _context.Users
                .Include(u => u.Warehouse)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return Unauthorized("User not found.");

            var today = DateTime.Today;

            var tomorrow = today.AddDays(1);
            var afterTomorrow = today.AddDays(2);
            var day2 = today.AddDays(2);      // inicio de pasado mañana
            var day4 = today.AddDays(4);

            // Base query: Available hoy y Future mañana
            var query = _context.Routes
                .Include(r => r.Zone)
                    .ThenInclude(z => z.Warehouse)
                        .ThenInclude(w => w.Companie)
                .Where(r =>
                    // Available: hoy o mañana  → [today, day2)
                    (r.routeStatus == RouteStatus.Available && r.Date >= today && r.Date < day2)
                    ||
                    // Future: +2 y +3 días      → [day2, day4)
                    (r.routeStatus == RouteStatus.Future && r.Date >= day2 && r.Date < day4)
                )
                .OrderBy(r => r.Date)
                .AsQueryable();

            // Regla de visibilidad:
            // - Sin CompanyId => filtrar por ciudad del usuario
            // - Con CompanyId => filtrar por Warehouse asignado
            if (user.CompanyId == null)
            {
                var userCity = user.Warehouse?.City;
                if (!string.IsNullOrWhiteSpace(userCity))
                {
                    query = query.Where(r => r.Zone.Warehouse.City == userCity);
                }
                // Si el usuario no tiene warehouse/city, no se filtra por ciudad (verá nada más la base query)
                // Puedes decidir retornar vacío en ese caso si lo prefieres.
            }
            else
            {
                if (user.WarehouseId.HasValue)
                {
                    query = query.Where(r => r.Zone.IdWarehouse == user.WarehouseId.Value);
                }
                else
                {
                    // Si tiene company pero no warehouse asignado, puedes decidir política:
                    // aquí no añadimos filtro extra (verá la base query) o retornar vacío.
                    // query = query.Where(r => false); // opción para forzar vacío
                }
            }

            var routes = await query
                .Select(r => new
                {
                    r.Id,
                    Zone = r.Zone != null ? r.Zone.ZoneCode : "Sin zona",
                    area = r.Zone != null ? r.Zone.Area : null,
                    zipCodes = r.Zone != null ? r.Zone.ZipCodesSerialized : null,
                    price = r.Zone != null ? r.Zone.PriceStop : (decimal?)null,
                    r.Volumen,
                    r.DeliveryStops,
                    RouteStatus = r.routeStatus == RouteStatus.Available ? "Available" :
                                  r.routeStatus == RouteStatus.Future ? "Future" : "Other",
                    r.Date,
                    LogoUrl = r.Zone != null && r.Zone.Warehouse != null && r.Zone.Warehouse.Companie != null
                                ? r.Zone.Warehouse.Companie.LogoUrl
                                : null
                })
                .ToListAsync();

            return Ok(routes);
        }

        public class ImportRouteParcelInfoRequest
        {
            public IFormFile File { get; set; } = null!;
            public int WarehouseId { get; set; }
        }

        public class ImportDailyRoutesRequest
        {
            public IFormFile File { get; set; } = null!;
            public int WarehouseId { get; set; }
        }

        [Authorize]
        [HttpPost("import-UniUni-DailyRoutes")]
        public async Task<IActionResult> ImportDailyRoutes(
    [FromForm] ImportDailyRoutesRequest req,
    CancellationToken ct)
        {
            if (req.File == null || req.File.Length == 0)
                return BadRequest(new { Message = "File required." });

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdStr, out var currentUserId);

            // 1) Leer Excel
            using var ms = new MemoryStream();
            await req.File.CopyToAsync(ms, ct);
            ms.Position = 0;

            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name.Equals("Daily", StringComparison.OrdinalIgnoreCase))
                  ?? wb.Worksheets.First();

            // 2) HeaderMap dinámico
            var headerRow = ws.Row(1);
            var headerMap = headerRow.CellsUsed()
                .ToDictionary(
                    c => c.GetString().Trim(),
                    c => c.Address.ColumnNumber,
                    StringComparer.OrdinalIgnoreCase);

            string S(IXLRow row, string col) =>
                headerMap.TryGetValue(col, out var i) ? row.Cell(i).GetString().Trim() : "";

            DateTime? D(IXLRow row, string col)
            {
                if (!headerMap.TryGetValue(col, out var i)) return null;
                var cell = row.Cell(i);
                if (cell.IsEmpty()) return null;
                if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime();
                return DateTime.TryParse(cell.GetString().Trim(), out var dt) ? dt : null;
            }

            int I(IXLRow row, string col)
            {
                if (!headerMap.TryGetValue(col, out var i)) return 0;
                var cell = row.Cell(i);
                if (cell.IsEmpty()) return 0;
                if (cell.DataType == XLDataType.Number) return (int)cell.GetDouble();
                return int.TryParse(cell.GetString().Trim(), out var v) ? v : 0;
            }

            double? Dbl(IXLRow row, string col)
            {
                if (!headerMap.TryGetValue(col, out var i)) return null;
                var cell = row.Cell(i);
                if (cell.IsEmpty()) return null;
                if (cell.DataType == XLDataType.Number) return cell.GetDouble();

                return double.TryParse(
                    cell.GetString().Trim(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v
                ) ? v : null;
            }

            var users = await _context.Users
                .AsNoTracking()
                .Where(u => !string.IsNullOrEmpty(u.IdentificationNumber))
                .Select(u => new { u.Id, u.IdentificationNumber })
                .ToListAsync(ct);

            var userByIdNumber = users
                .GroupBy(u => u.IdentificationNumber!.Trim())
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var created = 0;
            var updated = 0;
            var driverNotFound = new List<string>();

            var minDate = DateTime.MaxValue;
            var maxDate = DateTime.MinValue;
            var rawRows = new List<(string DriverId, DateTime Date, int Volume, int Stops, double? Price)>();

            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                var driverId = S(row, "driver_id");
                var date = D(row, "date");

                if (date == null)
                    continue;

                rawRows.Add((
                    driverId,
                    date.Value.Date,
                    I(row, "total_order_cnt"),
                    I(row, "first_order_cnt"),
                    Dbl(row, "total_delivery_price")
                ));

                if (date.Value < minDate) minDate = date.Value;
                if (date.Value > maxDate) maxDate = date.Value;
            }

            if (rawRows.Count == 0)
                return BadRequest(new { Message = "No valid rows found." });

            var existingRoutes = await _context.Routes
                .Where(r => r.WarehouseId == req.WarehouseId
                         && r.Date >= minDate
                         && r.Date <= maxDate.AddDays(1))
                .ToListAsync(ct);

            foreach (var row in rawRows)
            {
                int? userId = null;

                if (!string.IsNullOrWhiteSpace(row.DriverId))
                {
                    if (userByIdNumber.TryGetValue(row.DriverId, out var uid))
                        userId = uid;
                    else
                        driverNotFound.Add(row.DriverId);
                }

                var existing = existingRoutes.FirstOrDefault(r =>
                    r.WarehouseId == req.WarehouseId &&
                    r.Date.Date == row.Date.Date &&
                    r.UserId == userId);

                if (existing != null)
                {
                    existing.Volumen = row.Volume;
                    existing.DeliveryStops = row.Stops;
                    existing.PriceRoute = row.Price;
                    updated++;
                }
                else
                {
                    _context.Routes.Add(new Routes
                    {
                        WarehouseId = req.WarehouseId,
                        UserId = userId,
                        Date = row.Date,
                        Volumen = row.Volume,
                        DeliveryStops = row.Stops,
                        CNL = 0,
                        Attempts = 0,
                        routeStatus = RouteStatus.Completed,
                        PaymentType = Model.PaymentType.PerRoute,
                        PriceRoute = row.Price
                    });

                    created++;
                }
            }

            await _context.SaveChangesAsync(ct);

            var distinctDriverNotFound = driverNotFound
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await _auditService.LogAsync(new AuditLogDto
            {
                UserId = currentUserId,
                Action = AuditLogAction.ExcelImport,
                Entity = "Routes",
                Description =
                    $"UniUni Daily Routes imported. Warehouse={req.WarehouseId}, " +
                    $"Created={created}, Updated={updated}, Rows={rawRows.Count}, " +
                    $"DriversNotFound={distinctDriverNotFound.Count}",

                WarehouseId = req.WarehouseId,

                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    FileName = req.File.FileName,
                    WarehouseId = req.WarehouseId,
                    Worksheet = ws.Name,
                    DateRange = new
                    {
                        MinDate = minDate,
                        MaxDate = maxDate
                    },
                    Created = created,
                    Updated = updated,
                    TotalRows = rawRows.Count,
                    DriversNotFound = distinctDriverNotFound
                })
            });

            return Ok(new
            {
                created,
                updated,
                totalRows = rawRows.Count,
                driverNotFound = distinctDriverNotFound
            });
        }

        [Authorize]
        [HttpPost("route-parcel-info")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportRouteParcelInfo(
    [FromForm] ImportRouteParcelInfoRequest req,
    CancellationToken ct)
        {
            if (req.File == null || req.File.Length == 0)
                return BadRequest(new { Message = "Archivo requerido." });

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdStr, out var currentUserId);

            var warehouseId = req.WarehouseId;

            using var ms = new MemoryStream();
            await req.File.CopyToAsync(ms, ct);
            ms.Position = 0;

            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.First();

            var headerRow = ws.Row(1);
            var headerMap = headerRow.CellsUsed()
                .ToDictionary(
                    c => c.GetString().Trim(),
                    c => c.Address.ColumnNumber,
                    StringComparer.OrdinalIgnoreCase);

            string S(IXLRow row, string col) =>
                headerMap.TryGetValue(col, out var i) ? row.Cell(i).GetString().Trim() : "";

            DateTime? D(IXLRow row, string col)
            {
                if (!headerMap.TryGetValue(col, out var i)) return null;

                var cell = row.Cell(i);
                if (cell.IsEmpty()) return null;

                if (cell.DataType == XLDataType.DateTime)
                    return cell.GetDateTime();

                var s = cell.GetString().Trim();
                return DateTime.TryParse(s, out var dt) ? dt : null;
            }

            decimal? Dec(IXLRow row, string col)
            {
                if (!headerMap.TryGetValue(col, out var i)) return null;

                var cell = row.Cell(i);
                if (cell.IsEmpty()) return null;

                if (cell.DataType == XLDataType.Number)
                    return Convert.ToDecimal(cell.GetDouble());

                var s = cell.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(s)) return null;

                s = s.Replace(",", ".");

                return decimal.TryParse(
                    s,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var val
                ) ? val : null;
            }

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var rawRows = new List<RouteParcelRow>();

            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);

                var tracking = S(row, "TrackingNo");
                var routeCode = S(row, "Route");
                var planDate = D(row, "CompleteTime");

                if (string.IsNullOrWhiteSpace(tracking) ||
                    string.IsNullOrWhiteSpace(routeCode) ||
                    planDate == null)
                    continue;

                var statusEnum = MapPackageStatus(S(row, "FinalStatus"));

                rawRows.Add(new RouteParcelRow
                {
                    Tracking = tracking,
                    RouteCode = routeCode,
                    Date = planDate.Value.Date,

                    Poe = S(row, "POE"),
                    DspName = S(row, "DspName"),
                    DriverNameRaw = S(row, "DriverName"),

                    Address = S(row, "Address"),
                    Unit = S(row, "Unit"),
                    City = S(row, "City"),
                    State = S(row, "State"),
                    Zip = S(row, "ZipCode"),

                    Status = statusEnum,
                    Weight = Dec(row, "Weight")
                });
            }

            if (rawRows.Count == 0)
                return BadRequest(new { Message = "No hay filas válidas (TrackingNo/Route/planDeliveryDate)." });

            var minDate = rawRows.Min(x => x.Date);
            var maxDate = rawRows.Max(x => x.Date);

            var users = await _context.Users
                .AsNoTracking()
                .Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.LastName,
                    u.WarehouseId
                })
                .ToListAsync(ct);

            var filteredUsers = users
                .Where(u => u.WarehouseId == warehouseId)
                .ToList();

            var userMap = filteredUsers
                .GroupBy(u => NormalizeDriverFullName($"{u.Name} {u.LastName}"))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Id).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var groups = rawRows.GroupBy(x => new { x.RouteCode, x.Date });

            var existingRoutes = await _context.Routes
                .Where(r => r.WarehouseId == warehouseId
                         && r.Date.Date >= minDate
                         && r.Date.Date <= maxDate)
                .ToListAsync(ct);

            Routes? FindRoute(string routeCode, DateTime date) =>
                existingRoutes.FirstOrDefault(r =>
                    r.WarehouseId == warehouseId &&
                    r.Date.Date == date.Date &&
                    r.RouteCode == routeCode);

            var createdRoutes = 0;
            var updatedRoutes = 0;

            var driverNotFound = new List<object>();
            var driverAmbiguous = new List<object>();
            var driverAssigned = 0;

            foreach (var g in groups)
            {
                var routeCode = g.Key.RouteCode;
                var date = g.Key.Date;

                var volume = g
                    .Select(x => x.Tracking)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                var delivered = g.Where(x => x.Status == PackageStatus.CL);

                var stopGroups = delivered
                    .Select(x => new
                    {
                        x.Tracking,
                        StopKey = BuildStopKey(x)
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Tracking) &&
                                !string.IsNullOrWhiteSpace(x.StopKey))
                    .GroupBy(x => x.StopKey, StringComparer.OrdinalIgnoreCase);

                var stops = stopGroups.Count();

                var multiPackageStops = stopGroups.Count(sg =>
                    sg.Select(z => z.Tracking)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .Count() > 1);

                var extraPackagesInMultiStops = stopGroups.Sum(sg =>
                {
                    var c = sg.Select(z => z.Tracking)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    return c > 1 ? c - 1 : 0;
                });

                var cnl = g.Count(x => x.Status == PackageStatus.CNL);
                var attempts = g.Count(x => x.Status == PackageStatus.RTN);

                var driverRaw = g.Select(x => x.DriverNameRaw)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

                var driverKey = NormalizeDriverFullName(driverRaw);

                int? driverId = null;

                if (!string.IsNullOrWhiteSpace(driverKey))
                {
                    if (userMap.TryGetValue(driverKey, out var ids))
                    {
                        if (ids.Count == 1)
                        {
                            driverId = ids[0];
                        }
                        else
                        {
                            driverAmbiguous.Add(new
                            {
                                Date = date,
                                RouteCode = routeCode,
                                DriverName = driverRaw,
                                Normalized = driverKey,
                                CandidateUserIds = ids
                            });
                        }
                    }
                    else
                    {
                        var withoutMiddle = NormName(RemoveMiddleName(driverRaw));

                        if (userMap.TryGetValue(withoutMiddle, out var ids2))
                        {
                            if (ids2.Count == 1)
                            {
                                driverId = ids2[0];
                            }
                            else
                            {
                                driverAmbiguous.Add(new
                                {
                                    Date = date,
                                    RouteCode = routeCode,
                                    DriverName = driverRaw,
                                    Normalized = withoutMiddle,
                                    CandidateUserIds = ids2
                                });
                            }
                        }
                        else
                        {
                            driverNotFound.Add(new
                            {
                                Date = date,
                                RouteCode = routeCode,
                                DriverName = driverRaw,
                                Normalized = driverKey
                            });
                        }
                    }
                }
                else
                {
                    driverNotFound.Add(new
                    {
                        Date = date,
                        RouteCode = routeCode,
                        DriverName = driverRaw,
                        Normalized = driverKey
                    });
                }

                var route = FindRoute(routeCode, date);

                if (route == null)
                {
                    route = new Routes
                    {
                        WarehouseId = warehouseId,

                        Date = date,
                        RouteCode = routeCode,

                        DeliveryStops = stops,
                        Volumen = volume,
                        CNL = cnl,

                        Los = 0,
                        CustomerOnTime = 0,
                        BranchOnTime = 0,

                        Attempts = attempts,
                        PaymentType = PaymentType.PerStop,
                        routeStatus = driverId.HasValue
                            ? RouteStatus.Completed
                            : RouteStatus.Pending,
                        UserId = driverId
                    };

                    _context.Routes.Add(route);
                    existingRoutes.Add(route);
                    createdRoutes++;

                    if (driverId.HasValue)
                        driverAssigned++;
                }
                else
                {
                    route.DeliveryStops = stops;
                    route.Volumen = volume;
                    route.CNL = cnl;
                    route.Attempts = attempts;

                    if (driverId.HasValue)
                    {
                        route.UserId = driverId;
                        route.routeStatus = RouteStatus.Completed;
                        driverAssigned++;
                    }
                    else if (route.UserId == null)
                    {
                        route.routeStatus = RouteStatus.Pending;
                    }

                    updatedRoutes++;
                }

                _ = multiPackageStops;
                _ = extraPackagesInMultiStops;
            }

            await _context.SaveChangesAsync(ct);

            var routeLookup = existingRoutes
                .Where(r => r.WarehouseId == warehouseId && r.RouteCode != null)
                .ToDictionary(r => (r.RouteCode!, r.Date.Date), r => r.Id);

            var routeIds = routeLookup.Values.Distinct().ToList();

            var existingPackages = await _context.Packages
                .Where(p => routeIds.Contains((int)p.RoutesId))
                .ToListAsync(ct);

            var existingPackageMap = existingPackages.ToDictionary(
                p => $"{p.RoutesId}|{p.Tracking}".ToUpperInvariant(),
                p => p
            );

            var packagesAdded = 0;
            var packagesUpdated = 0;
            var skippedNoRoute = 0;

            foreach (var x in rawRows)
            {
                if (!routeLookup.TryGetValue((x.RouteCode, x.Date), out var routeId))
                {
                    skippedNoRoute++;
                    continue;
                }

                var fullAddress = string.IsNullOrWhiteSpace(x.Unit)
                    ? x.Address
                    : $"{x.Address} #{x.Unit}";

                var packageKey = $"{routeId}|{x.Tracking}".ToUpperInvariant();

                if (existingPackageMap.TryGetValue(packageKey, out var existingPackage))
                {
                    existingPackage.Address = fullAddress;
                    existingPackage.City = x.City;
                    existingPackage.State = x.State;
                    existingPackage.ZipCode = x.Zip;
                    existingPackage.IncidentDate = x.Date;
                    existingPackage.Weight = x.Weight;
                    existingPackage.Status = x.Status;

                    packagesUpdated++;
                    continue;
                }

                var newPackage = new Packages
                {
                    RoutesId = routeId,
                    Tracking = x.Tracking,
                    Address = fullAddress,
                    City = x.City,
                    State = x.State,
                    ZipCode = x.Zip,
                    IncidentDate = x.Date,
                    Weight = x.Weight,
                    Status = x.Status,
                    DaysElapsed = 0,
                    Notified = false,
                    ReviewStatus = ReviewStatus.Open
                };

                _context.Packages.Add(newPackage);
                existingPackageMap[packageKey] = newPackage;
                packagesAdded++;
            }

            await _context.SaveChangesAsync(ct);

            await _auditService.LogAsync(new AuditLogDto
            {
                UserId = currentUserId,
                Action = AuditLogAction.RouteParcelImport,
                Entity = "RouteParcelImport",

                Description =
                    $"Route Parcel Import completed. Warehouse={warehouseId}, " +
                    $"RowsRead={rawRows.Count}, " +
                    $"RoutesCreated={createdRoutes}, " +
                    $"RoutesUpdated={updatedRoutes}, " +
                    $"PackagesAdded={packagesAdded}, " +
                    $"PackagesUpdated={packagesUpdated}, " +
                    $"DriversAssigned={driverAssigned}, " +
                    $"DriversNotFound={driverNotFound.Count}",

                WarehouseId = warehouseId,

                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    FileName = req.File.FileName,
                    WarehouseId = warehouseId,

                    DateRange = new
                    {
                        MinDate = minDate,
                        MaxDate = maxDate
                    },

                    RowsRead = rawRows.Count,

                    RoutesCreated = createdRoutes,
                    RoutesUpdated = updatedRoutes,

                    PackagesAdded = packagesAdded,
                    PackagesUpdated = packagesUpdated,
                    SkippedNoRoute = skippedNoRoute,

                    DriverAssignedRoutes = driverAssigned,

                    DriverNotFound = driverNotFound
                        .Take(100)
                        .ToList(),

                    DriverAmbiguous = driverAmbiguous
                        .Take(100)
                        .ToList()
                })
            });

            return Ok(new
            {
                Message = "Import OK",
                WarehouseId = warehouseId,
                DateRange = new { minDate, maxDate },
                RowsRead = rawRows.Count,
                RoutesCreated = createdRoutes,
                RoutesUpdated = updatedRoutes,
                PackagesAdded = packagesAdded,
                PackagesUpdated = packagesUpdated,
                SkippedNoRoute = skippedNoRoute,

                DriverAssignedRoutes = driverAssigned,
                DriverNotFound = driverNotFound.Take(50),
                DriverAmbiguous = driverAmbiguous.Take(50)
            });
        }

        string RemoveMiddleName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "";

            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length <= 2)
                return fullName;

            return $"{parts.First()} {parts.Last()}";
        }

        // Métodos auxiliares para parsear valores de forma segura

        private int SafeParseInt(string value) => int.TryParse(value, out int result) ? result : 0;
        private double SafeParseDouble(string value) => double.TryParse(value, out double result) ? result : 0;
        static string Clean(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim().ToUpperInvariant();

            // normaliza espacios
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");

            // opcional: limpia signos comunes
            s = s.Replace(".", "").Replace(",", "");

            return s;
        }

        private static PackageStatus MapPackageStatus(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return PackageStatus.RD;

            var s = raw.Trim().ToUpperInvariant();

            return s switch
            {
                "DELIVERED" => PackageStatus.CL,

                // Intento fallido (no entregado)
                "FAILED_ATTEMPT" => PackageStatus.UD,

                // Devuelto por error de clasificación
                "MIS_SORT_RETURN" => PackageStatus.RTN,

                // Devuelto al centro
                "RETURNED_SORT_CENTER" => PackageStatus.RTN,

                _ => PackageStatus.OD
            };
        }


        static string BuildStopKey(RouteParcelRow x)
        {
            // Ajusta según la calidad de tus datos:
            // - Si Unit viene separado, úsalo.
            // - Si Address ya viene con apt incluido, igual sirve.
            // - Zip ayuda a evitar colisiones.
            var addr = Clean(x.Address);
            var unit = Clean(x.Unit);
            var city = Clean(x.City);
            var state = Clean(x.State);
            var zip = Clean(x.Zip);

            // Si tienes "Unit" vacío, no lo metas para no crear llaves raras
            // pero si existe, úsalo porque un mismo address con apt distintos son stops distintos.
            return string.IsNullOrWhiteSpace(unit)
                ? $"{addr}|{city}|{state}|{zip}"
                : $"{addr}|UNIT:{unit}|{city}|{state}|{zip}";
        }

        private string GetWarehouseCity(int warehouseId)
        {
            var warehouse = _context.Warehouses.FirstOrDefault(w => w.Id == warehouseId);
            return warehouse != null ? $"{warehouse.Company} - {warehouse.City}" : null;
        }
        private string GetReadableStatus(RouteStatus status)
        {
            return status switch
            {
                RouteStatus.Pending => "Pending",
                RouteStatus.Assigned => "Assigned",
                RouteStatus.InProgress => "In Progress",
                RouteStatus.Completed => "Completed",
                RouteStatus.Cancelled => "Cancelled",
                RouteStatus.Delayed => "Delayed",
                RouteStatus.Future => "Future",
                RouteStatus.Created => "Created",
                RouteStatus.Available => "Available",
                RouteStatus.Loading => "Loading",
                RouteStatus.PendingCompletion => "PendingCompletion",
                _ => status.ToString()
            };
        }

        private static string NormName(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";

            s = s.Trim();

            // deja letras/números/espacios
            s = Regex.Replace(s, @"[^\p{L}\p{N}\s]", " ");
            s = Regex.Replace(s, @"\s+", " ");
            s = s.ToLowerInvariant();

            // quita acentos
            var normalized = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var ch in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC).Trim();
        }

        // Soporta "Last, First" y "First Last"
        private static string NormalizeDriverFullName(string raw)
        {
            raw = raw?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(raw)) return "";

            // si viene "Apellido, Nombre"
            if (raw.Contains(","))
            {
                var parts = raw.Split(',', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                    raw = $"{parts[1].Trim()} {parts[0].Trim()}";
            }

            return NormName(raw);
        }

        [Authorize]
        [HttpPost("routes/{routeId}/bonus")]
        public async Task<IActionResult> AddRouteBonus(int routeId, [FromBody] AddRouteBonusDto dto)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            if (userRole != "Admin" && userRole != "Manager")
                return Forbid();

            var route = await _context.Routes.FindAsync(routeId);
            if (route == null)
                return NotFound(new { Message = "Route not found." });

            var bonus = new RouteBonus
            {
                RouteId          = routeId,
                Type             = dto.Type,
                Amount           = dto.Amount,
                Note             = dto.Note,
                AssignedByUserId = userId,
                AssignedAt       = route.Date,
                IsActive         = true,
                ApprovalToken    = Guid.NewGuid().ToString("N")
            };

            _context.RouteBonuses.Add(bonus);
            await _context.SaveChangesAsync();

            try
            {
                var warehouseData = route.WarehouseId.HasValue
                    ? await _context.Warehouses
                        .Where(w => w.Id == route.WarehouseId.Value)
                        .Select(w => new { w.Name, w.CompanyId })
                        .FirstOrDefaultAsync()
                    : null;

                var companyId = warehouseData?.CompanyId ?? 0;
                var warehouseIds = route.WarehouseId.HasValue ? new[] { route.WarehouseId.Value } : null;

                if (companyId > 0)
                {
                    var recipients = await _communicationRecipients.GetRecipientsForEventAsync(
                        companyId: companyId,
                        warehouseIds: warehouseIds,
                        eventType: CommunicationEventTypes.RouteBonusPending,
                        channel: CommunicationChannels.Email);

                    var driver = route.UserId.HasValue
                        ? await _context.Users
                            .Where(u => u.Id == route.UserId.Value)
                            .Select(u => new { u.Name, u.LastName })
                            .FirstOrDefaultAsync()
                        : null;

                    var assignedByUser = await _context.Users
                        .Where(u => u.Id == userId)
                        .Select(u => new { u.Name, u.LastName })
                        .FirstOrDefaultAsync();

                    var placeholders = new Dictionary<string, string>
                    {
                        { "RouteCode",     route.RouteCode ?? route.Id.ToString() },
                        { "Date",          DateTime.UtcNow.ToString("MMMM dd, yyyy", new System.Globalization.CultureInfo("en-US")) },
                        { "BonusType",     bonus.Type.ToString() },
                        { "Amount",        bonus.Amount.ToString("C") },
                        { "Note",          bonus.Note ?? "" },
                        { "AssignedBy",    assignedByUser != null ? $"{assignedByUser.Name} {assignedByUser.LastName}".Trim() : userId.ToString() },
                        { "DriverName",    driver != null ? $"{driver.Name} {driver.LastName}".Trim() : "N/A" },
                        { "WarehouseName", warehouseData?.Name ?? "" },
                        { "ApproveUrl",    $"https://ttologistics.online/api/routes/bonus/{bonus.Id}/approve-token?token={bonus.ApprovalToken}" },
                        { "RejectUrl",     $"https://ttologistics.online/api/routes/bonus/{bonus.Id}/reject-token?token={bonus.ApprovalToken}" }
                    };

                    foreach (var email in recipients
                        .Select(r => r.Email)
                        .Where(e => !string.IsNullOrWhiteSpace(e))
                        .Select(e => e!)
                        .Distinct())
                    {
                        await _emailService.SendEmailAsync(
                            toEmail: email,
                            subject: $"Route Bonus Pending Approval - Route {route.Id}",
                            "RouteBonusNotification.cshtml",
                            placeholders: placeholders,
                            copy: false);
                    }
                }
            }
            catch { /* no bloquear la respuesta si falla el email */ }

            return Ok(bonus);
        }

        [HttpGet("routes/bonus/{bonusId}/approve-token")]
        public async Task<IActionResult> ApproveBonusByToken(int bonusId, [FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest(new { Message = "Token required." });

            var bonus = await _context.RouteBonuses.FindAsync(bonusId);
            if (bonus == null) return NotFound(new { Message = "Bonus not found." });
            if (bonus.ApprovalToken != token) return Unauthorized(new { Message = "Invalid token." });
            if (bonus.Status != RouteBonusStatus.Pending)
                return BadRequest(new { Message = $"Bonus is already {bonus.Status}." });

            bonus.Status    = RouteBonusStatus.Approved;
            bonus.ApprovedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Bonus approved successfully." });
        }

        [HttpGet("routes/bonus/{bonusId}/reject-token")]
        public async Task<IActionResult> RejectBonusByToken(int bonusId, [FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest(new { Message = "Token required." });

            var bonus = await _context.RouteBonuses.FindAsync(bonusId);
            if (bonus == null) return NotFound(new { Message = "Bonus not found." });
            if (bonus.ApprovalToken != token) return Unauthorized(new { Message = "Invalid token." });
            if (bonus.Status != RouteBonusStatus.Pending)
                return BadRequest(new { Message = $"Bonus is already {bonus.Status}." });

            bonus.Status    = RouteBonusStatus.Rejected;
            bonus.ApprovedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Bonus rejected successfully." });
        }

        [Authorize]
        [HttpPut("routes/bonus/{bonusId}/approve")]
        public async Task<IActionResult> ApproveRouteBonus(int bonusId)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var userRole    = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            if (userRole != "Admin" && userRole != "Manager")
                return Forbid();

            var bonus = await _context.RouteBonuses.FindAsync(bonusId);
            if (bonus == null)
                return NotFound(new { Message = "Bonus not found." });

            if (bonus.Status != RouteBonusStatus.Pending)
                return BadRequest(new { Message = $"Bonus is already {bonus.Status}." });

            bonus.Status            = RouteBonusStatus.Approved;
            bonus.ApprovedByUserId  = userId;
            bonus.ApprovedAt        = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(bonus);
        }

        [Authorize]
        [HttpPut("routes/bonus/{bonusId}/reject")]
        public async Task<IActionResult> RejectRouteBonus(int bonusId)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var userRole    = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            if (userRole != "Admin" && userRole != "Manager")
                return Forbid();

            var bonus = await _context.RouteBonuses.FindAsync(bonusId);
            if (bonus == null)
                return NotFound(new { Message = "Bonus not found." });

            if (bonus.Status != RouteBonusStatus.Pending)
                return BadRequest(new { Message = $"Bonus is already {bonus.Status}." });

            bonus.Status            = RouteBonusStatus.Rejected;
            bonus.ApprovedByUserId  = userId;
            bonus.ApprovedAt        = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(bonus);
        }

        [HttpGet("users/find-similar-import-name")]
        public async Task<IActionResult> FindSimilarImportName([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Name is required.");

            var parts = name
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return Ok(new List<object>());

            var firstName = parts.First().Trim().ToLower();
            var lastName = parts.Length > 1 ? parts.Last().Trim().ToLower() : "";

            var users = await _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive && u.Name != null && u.LastName != null)
                .Select(u => new
                {
                    u.Id,
                    Name = u.Name!,
                    LastName = u.LastName!,
                    FullName = ((u.Name ?? "") + " " + (u.LastName ?? "")).Trim()
                })
                .ToListAsync();

            var matches = users
                .Where(u =>
                    u.Name.Trim().ToLower() == firstName ||
                    u.LastName.Trim().ToLower() == lastName ||
                    (u.Name.Trim().ToLower() == firstName && u.LastName.Trim().ToLower() == lastName) ||
                    u.FullName.ToLower().Contains(firstName) ||
                    (!string.IsNullOrWhiteSpace(lastName) && u.FullName.ToLower().Contains(lastName))
                )
                .OrderByDescending(u => u.Name.Trim().ToLower() == firstName && u.LastName.Trim().ToLower() == lastName)
                .ThenBy(u => u.FullName)
                .Take(10)
                .ToList();

            return Ok(matches);
        }
        [HttpPost("users/save-import-match")]
        public async Task<IActionResult> SaveImportMatch([FromBody] SaveImportMatchDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ImportedName) || dto.UserId <= 0)
                return BadRequest("ImportedName and UserId are required.");

            var user = await _context.Users.FirstOrDefaultAsync(x => x.Id == dto.UserId);
            if (user == null)
                return NotFound("User not found.");

            var normalized = dto.ImportedName.Trim().ToLower();

            var existing = await _context.Set<UserImportMatch>()
                .FirstOrDefaultAsync(x => x.ImportedNameNormalized == normalized);

            if (existing != null)
            {
                existing.UserId = dto.UserId;
                existing.ImportedName = dto.ImportedName.Trim();
            }
            else
            {
                _context.Set<UserImportMatch>().Add(new UserImportMatch
                {
                    UserId = dto.UserId,
                    ImportedName = dto.ImportedName.Trim(),
                    ImportedNameNormalized = normalized,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Match saved successfully." });
        }

        public class SaveImportMatchDto
        {
            public string ImportedName { get; set; } = string.Empty;
            public int UserId { get; set; }
        }
        public class AddRouteBonusDto
        {
            public RouteBonusType Type { get; set; }

            [Column(TypeName = "decimal(18,2)")]
            public decimal Amount { get; set; }

            public string? Note { get; set; }
        }
        private sealed class RouteParcelRow
        {
            public string Tracking { get; set; } = "";
            public string RouteCode { get; set; } = "";
            public DateTime Date { get; set; }

            public string? Poe { get; set; }
            public string? DspName { get; set; }
            public string? DriverNameRaw { get; set; }

            public string? Address { get; set; }
            public string? Unit { get; set; }
            public string? City { get; set; }
            public string? State { get; set; }
            public string? Zip { get; set; }
            public PackageStatus Status { get; set; }
            public decimal? Weight { get; set; }  // ✅ solo el número
        }


        private sealed class RowDto
        {
            public string Tracking { get; set; } = "";
            public string RouteCode { get; set; } = "";
            public DateTime Date { get; set; }
            public string? Address { get; set; }
            public string? Unit { get; set; }
            public string? City { get; set; }
            public string? State { get; set; }
            public string? Zip { get; set; }
            public string? FinalStatus { get; set; }
        }

        private static RouteStatus? ParseRouteStatus(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var s = input.Trim();

            // ¿vino como número?
            if (int.TryParse(s, out var n) && Enum.IsDefined(typeof(RouteStatus), n))
                return (RouteStatus)n;

            var norm = s.Replace(" ", "").Replace("-", "").ToLowerInvariant();
            return norm switch
            {
                "pending" => RouteStatus.Pending,
                "assigned" => RouteStatus.Assigned,
                "inprogress" => RouteStatus.InProgress,
                "completed" => RouteStatus.Completed,
                "cancelled" or "canceled" => RouteStatus.Cancelled,
                "delayed" => RouteStatus.Delayed,
                "future" => RouteStatus.Future,
                "created" => RouteStatus.Created,
                "available" => RouteStatus.Available,
                "loading" => RouteStatus.Loading,
                "PendingCompletion" => RouteStatus.PendingCompletion,
                _ => (RouteStatus?)null
            };
        }
        [HttpGet("my-assigned")]
        public async Task<ActionResult<IEnumerable<object>>> GetMyAssignedRoutes()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Invalid user ID.");

            // (Opcional) Si necesitas datos del usuario para algo futuro
            var user = await _context.Users
                .Include(u => u.Warehouse)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return Unauthorized("User not found.");

            var today = DateTime.Today;
            var day4 = today.AddDays(4); // hoy, mañana, pasado y +3 días (4 días en total)

            // Rutas ASIGNADAS al driver actual en el rango [today, day4)
            var query = _context.Routes
                .Include(r => r.Zone)
                    .ThenInclude(z => z.Warehouse)
                        .ThenInclude(w => w.Companie)
                .Where(r => r.UserId == userId
                            && r.Date >= today
                            && r.Date < day4)
                .OrderBy(r => r.Date)
                .AsQueryable();

            var routes = await query
                .Select(r => new
                {
                    r.Id,
                    Zone = r.Zone != null ? r.Zone.ZoneCode : "Sin zona",
                    area = r.Zone != null ? r.Zone.Area : null,
                    zipCodes = r.Zone != null ? r.Zone.ZipCodesSerialized : null,
                    price = r.Zone != null ? r.Zone.PriceStop : (decimal?)null,
                    r.Volumen,
                    r.DeliveryStops,

                    // Mapeo de estado en texto (mismo estilo que Available/Future)
                    RouteStatus =
                        r.routeStatus == RouteStatus.Assigned ? "Assigned" :
                        r.routeStatus == RouteStatus.Loading ? "Loading" :
                        r.routeStatus == RouteStatus.InProgress ? "In Progress" :
                        r.routeStatus == RouteStatus.Completed ? "Completed" :
                        r.routeStatus == RouteStatus.Future ? "Future" :
                        r.routeStatus == RouteStatus.Available ? "Available" : "Other",

                    r.Date,

                    // Logo desde la compañía del warehouse de la zona
                    LogoUrl = r.Zone != null && r.Zone.Warehouse != null && r.Zone.Warehouse.Companie != null
                        ? r.Zone.Warehouse.Companie.LogoUrl
                        : null
                })
                .ToListAsync();

            return Ok(routes);
        }

        [Authorize(Roles = "Admin,CompanyOwner,Manager,Assistant")]
        [HttpPost("upload-swiftx-dsp-summary/{warehouseId:int}")]
        public async Task<IActionResult> UploadSwiftXDspSummary(
    [FromRoute] int warehouseId,
    IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded." });

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdStr, out var currentUserId);

            var warehouse = await _context.Warehouses
                .FirstOrDefaultAsync(w => w.Id == warehouseId);

            if (warehouse == null)
                return NotFound(new { message = "Warehouse not found." });

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.First();
            var headerRow = worksheet.Row(1);

            int GetColumnIndex(params string[] possibleNames)
            {
                foreach (var cell in headerRow.CellsUsed())
                {
                    var value = cell.GetString().Trim();

                    foreach (var name in possibleNames)
                    {
                        if (value.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return cell.Address.ColumnNumber;
                    }
                }

                return -1;
            }

            int GetIntCellValue(IXLCell cell)
            {
                if (cell.DataType == XLDataType.Number)
                    return Convert.ToInt32(cell.GetDouble());

                var text = cell.GetString().Trim().Replace(",", "");

                if (int.TryParse(text, out var value))
                    return value;

                return 0;
            }

            var dateCol = GetColumnIndex("Fecha de entrega", "Delivery Date");
            var driverIdCol = GetColumnIndex("ID del conductor", "Driver ID");
            var driverNameCol = GetColumnIndex("Nombre del conductor", "Driver Name");
            var routeCodeCol = GetColumnIndex("Nombre de la ruta", "Route Name");
            var volumeCol = GetColumnIndex("Cantidad de paquetes entregados");
            var deliveryStopsCol = GetColumnIndex("Paquetes entregados (precio estándar)");

            if (dateCol == -1 ||
                driverIdCol == -1 ||
                routeCodeCol == -1 ||
                volumeCol == -1 ||
                deliveryStopsCol == -1)
            {
                return BadRequest(new
                {
                    message = "Invalid SwiftX Excel format.",
                    requiredColumns = new[]
                    {
                "Fecha de entrega",
                "ID del conductor",
                "Nombre de la ruta",
                "Cantidad de paquetes entregados",
                "Paquetes entregados (precio estándar)"
            }
                });
            }

            var routesCreated = 0;
            var routesUpdated = 0;
            var rowsRead = 0;

            var driverNotFound = new List<object>();
            var errors = new List<object>();

            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

            for (int rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var row = worksheet.Row(rowNumber);

                try
                {
                    var dateText = row.Cell(dateCol).GetString().Trim();
                    var driverCode = row.Cell(driverIdCol).GetString().Trim();
                    var routeCode = row.Cell(routeCodeCol).GetString().Trim();

                    var driverName = driverNameCol != -1
                        ? row.Cell(driverNameCol).GetString().Trim()
                        : "";

                    if (string.IsNullOrWhiteSpace(dateText) ||
                        string.IsNullOrWhiteSpace(driverCode) ||
                        string.IsNullOrWhiteSpace(routeCode))
                    {
                        continue;
                    }

                    if (!DateTime.TryParse(dateText, out var deliveryDate))
                    {
                        errors.Add(new
                        {
                            row = rowNumber,
                            message = "Invalid date.",
                            value = dateText
                        });

                        continue;
                    }

                    var volumenPackages = GetIntCellValue(row.Cell(volumeCol));
                    var deliveryStops = GetIntCellValue(row.Cell(deliveryStopsCol));

                    if (volumenPackages <= 0 && deliveryStops <= 0)
                        continue;

                    rowsRead++;

                    var user = await _context.Users
                        .FirstOrDefaultAsync(u =>
                            u.IdentificationNumber == driverCode &&
                            u.WarehouseId == warehouseId);

                    if (user == null)
                    {
                        driverNotFound.Add(new
                        {
                            row = rowNumber,
                            driverId = driverCode,
                            driverName,
                            routeCode,
                            date = deliveryDate.ToString("yyyy-MM-dd")
                        });
                    }

                    var userId = user?.Id;

                    var existingRoute = await _context.Routes
                        .FirstOrDefaultAsync(r =>
                            r.Date.Date == deliveryDate.Date &&
                            r.WarehouseId == warehouseId &&
                            r.RouteCode == routeCode &&
                            r.UserId == userId);

                    if (existingRoute != null)
                    {
                        existingRoute.UserId = userId ?? existingRoute.UserId;
                        existingRoute.Volumen = volumenPackages;
                        existingRoute.DeliveryStops = deliveryStops;
                        existingRoute.Attempts = 0;

                        existingRoute.Los = 100;
                        existingRoute.CustomerOnTime = 100;
                        existingRoute.BranchOnTime = 100;
                        existingRoute.CNL = 0;

                        existingRoute.routeStatus = userId.HasValue
                            ? RouteStatus.Completed
                            : RouteStatus.PendingCompletion;

                        existingRoute.PaymentType = PaymentType.PerStop;
                        existingRoute.WarehouseId = warehouseId;

                        routesUpdated++;
                    }
                    else
                    {
                        _context.Routes.Add(new Routes
                        {
                            Date = deliveryDate.Date,
                            UserId = userId,
                            WarehouseId = warehouseId,
                            RouteCode = routeCode,

                            Volumen = volumenPackages,
                            DeliveryStops = deliveryStops,
                            Attempts = 0,

                            Los = 100,
                            CustomerOnTime = 100,
                            BranchOnTime = 100,
                            CNL = 0,

                            routeStatus = userId.HasValue
                                ? RouteStatus.Completed
                                : RouteStatus.PendingCompletion,

                            PaymentType = PaymentType.PerStop
                        });

                        routesCreated++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new
                    {
                        row = rowNumber,
                        message = ex.Message
                    });
                }
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(new AuditLogDto
            {
                UserId = currentUserId,
                Action = AuditLogAction.SwiftXDspSummaryImport,
                Entity = "Routes",
                WarehouseId = warehouseId,
                WarehouseName = $"{warehouse.Company} - {warehouse.City}",

                Description =
                    $"SwiftX DSP summary imported. Warehouse={warehouseId}, " +
                    $"RowsRead={rowsRead}, RoutesCreated={routesCreated}, " +
                    $"RoutesUpdated={routesUpdated}, DriversNotFound={driverNotFound.Count}, " +
                    $"Errors={errors.Count}",

                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    FileName = file.FileName,
                    WarehouseId = warehouseId,
                    Warehouse = new
                    {
                        warehouse.Id,
                        warehouse.Company,
                        warehouse.City
                    },
                    RowsRead = rowsRead,
                    RoutesCreated = routesCreated,
                    RoutesUpdated = routesUpdated,
                    DriversNotFoundCount = driverNotFound.Count,
                    DriverNotFound = driverNotFound.Take(100).ToList(),
                    ErrorsCount = errors.Count,
                    Errors = errors.Take(100).ToList()
                })
            });

            return Ok(new
            {
                message = "SwiftX DSP summary imported successfully.",
                warehouseId,
                rowsRead,
                routesCreated,
                routesUpdated,
                driversNotFoundCount = driverNotFound.Count,
                driverNotFound,
                errors
            });
        }


        [Authorize(Roles = "Admin,CompanyOwner,Manager,Assistant")]
        [HttpPost("upload-route-manifest-pdf/{warehouseId:int}")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadRouteManifestPdf(
    [FromRoute] int warehouseId,
    [FromForm] List<IFormFile> files,
    CancellationToken ct)
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { message = "No files uploaded." });

            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdStr, out var currentUserId);

            var warehouse = await _context.Warehouses
                .FirstOrDefaultAsync(w => w.Id == warehouseId, ct);

            if (warehouse == null)
                return NotFound(new { message = "Warehouse not found." });

            var routesCreated = 0;
            var routesUpdated = 0;
            var packagesAdded = 0;
            var packagesUpdated = 0;
            var rowsRead = 0;

            var driverNotFound = new List<object>();
            var errors = new List<object>();

            foreach (var file in files)
            {
                if (file == null || file.Length == 0)
                    continue;

                if (Path.GetExtension(file.FileName).ToLower() != ".pdf")
                {
                    errors.Add(new
                    {
                        file = file.FileName,
                        message = "Invalid file type. Only PDF files are allowed."
                    });
                    continue;
                }

                try
                {
                    using var stream = new MemoryStream();
                    await file.CopyToAsync(stream, ct);
                    stream.Position = 0;

                    var text = ExtractPdfText(stream);

                    var routeDate = ExtractDate(text);
                    var driverCode = ExtractDriver(text);

                    if (routeDate == null || string.IsNullOrWhiteSpace(driverCode))
                    {
                        errors.Add(new
                        {
                            file = file.FileName,
                            message = "Could not read Date or Driver from PDF."
                        });
                        continue;
                    }

                    var pdfRows = ExtractPackageRows(text);

                    if (pdfRows.Count == 0)
                    {
                        errors.Add(new
                        {
                            file = file.FileName,
                            message = "No valid package rows found."
                        });
                        continue;
                    }

                    rowsRead += pdfRows.Count;

                    var user = await _context.Users
                        .FirstOrDefaultAsync(u =>
                            u.IdentificationNumber == driverCode &&
                            u.WarehouseId == warehouseId,
                            ct);

                    if (user == null)
                    {
                        driverNotFound.Add(new
                        {
                            file = file.FileName,
                            driverCode,
                            date = routeDate.Value.ToString("yyyy-MM-dd")
                        });
                    }

                    var trackingList = pdfRows
                        .Where(x => !string.IsNullOrWhiteSpace(x.OrderId))
                        .Select(x => x.OrderId.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var volume = trackingList.Count;

                    var stops = pdfRows
                        .Where(x => !string.IsNullOrWhiteSpace(x.Address))
                        .Select(x => NormalizeAddress(x.Address))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    var existingRoute = await _context.Routes
                        .FirstOrDefaultAsync(r =>
                            r.WarehouseId == warehouseId &&
                            r.Date.Date == routeDate.Value.Date &&
                            r.RouteCode == driverCode,
                            ct);

                    Routes route;

                    if (existingRoute == null)
                    {
                        route = new Routes
                        {
                            WarehouseId = warehouseId,
                            Date = routeDate.Value.Date,

                            RouteCode = driverCode,
                            DriverIdentificationNumber = driverCode,
                            UserId = user?.Id,

                            Volumen = volume,
                            DeliveryStops = stops,
                            Attempts = 0,
                            CNL = 0,

                            Los = 100,
                            CustomerOnTime = 100,
                            BranchOnTime = 100,

                            PaymentType = PaymentType.PerStop,
                            routeStatus = user != null
                                ? RouteStatus.Completed
                                : RouteStatus.PendingCompletion
                        };

                        _context.Routes.Add(route);
                        await _context.SaveChangesAsync(ct);

                        routesCreated++;
                    }
                    else
                    {
                        route = existingRoute;

                        route.UserId = user?.Id ?? route.UserId;
                        route.DriverIdentificationNumber = driverCode;

                        route.Volumen = volume;
                        route.DeliveryStops = stops;
                        route.Attempts = 0;
                        route.CNL = 0;

                        route.Los = 100;
                        route.CustomerOnTime = 100;
                        route.BranchOnTime = 100;

                        route.PaymentType = PaymentType.PerStop;
                        route.routeStatus = route.UserId.HasValue
                            ? RouteStatus.Completed
                            : RouteStatus.Pending;

                        routesUpdated++;
                    }

                    var existingPackages = await _context.Packages
                        .Where(p => p.RoutesId == route.Id)
                        .ToListAsync(ct);

                    var existingPackageMap = existingPackages
                        .Where(p => !string.IsNullOrWhiteSpace(p.Tracking))
                        .ToDictionary(
                            p => p.Tracking.Trim().ToUpperInvariant(),
                            p => p
                        );

                    foreach (var row in pdfRows)
                    {
                        if (string.IsNullOrWhiteSpace(row.OrderId))
                            continue;

                        var tracking = row.OrderId.Trim();
                        var packageKey = tracking.ToUpperInvariant();

                        var parsedAddress = ParseAddress(row.Address);

                        if (existingPackageMap.TryGetValue(packageKey, out var existingPackage))
                        {
                            existingPackage.Address = parsedAddress.Address;
                            existingPackage.City = parsedAddress.City;
                            existingPackage.State = parsedAddress.State;
                            existingPackage.ZipCode = parsedAddress.ZipCode;
                            existingPackage.IncidentDate = routeDate.Value.Date;
                            existingPackage.Status = PackageStatus.CL;

                            packagesUpdated++;
                            continue;
                        }

                        var newPackage = new Packages
                        {
                            RoutesId = route.Id,
                            Tracking = tracking,

                            Address = parsedAddress.Address,
                            City = parsedAddress.City,
                            State = parsedAddress.State,
                            ZipCode = parsedAddress.ZipCode,

                            IncidentDate = routeDate.Value.Date,
                            Status = PackageStatus.CL,
                            DaysElapsed = 0,
                            Notified = false,
                            ReviewStatus = ReviewStatus.Open
                        };

                        _context.Packages.Add(newPackage);
                        existingPackageMap[packageKey] = newPackage;

                        packagesAdded++;
                    }

                    await _context.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    errors.Add(new
                    {
                        file = file.FileName,
                        message = ex.Message,
                        innerException = ex.InnerException?.Message
                    });
                }
            }

            await _auditService.LogAsync(new AuditLogDto
            {
                UserId = currentUserId,
                Action = AuditLogAction.RouteParcelImport,
                Entity = "Routes",
                WarehouseId = warehouseId,
                WarehouseName = $"{warehouse.Company} - {warehouse.City}",

                Description =
                    $"Route Manifest PDF import completed. Warehouse={warehouseId}, " +
                    $"Files={files.Count}, RowsRead={rowsRead}, RoutesCreated={routesCreated}, " +
                    $"RoutesUpdated={routesUpdated}, PackagesAdded={packagesAdded}, " +
                    $"PackagesUpdated={packagesUpdated}, DriversNotFound={driverNotFound.Count}, Errors={errors.Count}",

                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Files = files.Select(f => f.FileName).ToList(),
                    WarehouseId = warehouseId,
                    RoutesCreated = routesCreated,
                    RoutesUpdated = routesUpdated,
                    PackagesAdded = packagesAdded,
                    PackagesUpdated = packagesUpdated,
                    RowsRead = rowsRead,
                    DriverNotFound = driverNotFound.Take(100).ToList(),
                    Errors = errors.Take(100).ToList()
                })
            });

            return Ok(new
            {
                message = "Route manifest PDF imported successfully.",
                warehouseId,
                rowsRead,
                routesCreated,
                routesUpdated,
                packagesAdded,
                packagesUpdated,
                driversNotFoundCount = driverNotFound.Count,
                driverNotFound,
                errors
            });
        }

        [HttpGet("test-business-report")]
        public async Task<IActionResult> TestBusinessReport(
    string username,
    string password,
    int driverId = 1084,
    string beginDate = "2026-06-13",
    string endDate = "2026-06-13")
        {
            try
            {
                using var playwright = await Playwright.CreateAsync();

                await using var browser = await playwright.Chromium.LaunchAsync(new()
                {
                    Headless = false,
                    Channel = "chrome",
                    SlowMo = 100
                });

                var page = await browser.NewPageAsync(new()
                {
                    ViewportSize = new ViewportSize
                    {
                        Width = 1366,
                        Height = 768
                    }
                });

                await page.GotoAsync(
                    "https://fastrac.ontrac.com/identityserver/Account/Login",
                    new()
                    {
                        WaitUntil = WaitUntilState.NetworkIdle,
                        Timeout = 60000
                    });

                // Abrir panel RSP Login
                await page.ClickAsync("a[data-target='#collapseOne']", new()
                {
                    Force = true
                });

                await page.WaitForTimeoutAsync(1000);

                await page.WaitForSelectorAsync("#Username");
                await page.WaitForSelectorAsync("#Password");

                // Llenar credenciales simulando usuario real
                await page.EvaluateAsync(@"({ username, password }) => {

            const user = document.querySelector('#Username');
            const pass = document.querySelector('#Password');
            const btn = document.querySelector('#loginBtn');

            user.focus();
            user.value = username;
            user.classList.add('has-val');

            user.dispatchEvent(new KeyboardEvent('keydown', { bubbles:true }));
            user.dispatchEvent(new KeyboardEvent('keyup', { bubbles:true }));
            user.dispatchEvent(new Event('input', { bubbles:true }));
            user.dispatchEvent(new Event('change', { bubbles:true }));
            user.blur();

            pass.focus();
            pass.value = password;
            pass.classList.add('has-val');

            pass.dispatchEvent(new KeyboardEvent('keydown', { bubbles:true }));
            pass.dispatchEvent(new KeyboardEvent('keyup', { bubbles:true }));
            pass.dispatchEvent(new Event('input', { bubbles:true }));
            pass.dispatchEvent(new Event('change', { bubbles:true }));
            pass.blur();

            if(btn){
                btn.disabled = false;
                btn.removeAttribute('disabled');
            }

        }", new
                {
                    username,
                    password
                });

                await page.WaitForTimeoutAsync(1500);

                var isDisabled = await page.EvaluateAsync<bool>(
                    "() => document.querySelector('#loginBtn')?.disabled ?? true");

                if (isDisabled)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Login button still disabled."
                    });
                }

                // Click en botón login real
                await page.ClickAsync("#loginBtn", new()
                {
                    Force = true,
                    Timeout = 30000
                });

                await page.WaitForTimeoutAsync(8000);

                var afterLoginUrl = page.Url;

                if (afterLoginUrl.Contains("Account/Login"))
                {
                    var loginErrorText = await page.EvaluateAsync<string>(@"() => {
                const el = document.querySelector('#loginErrorText');
                return el ? el.innerText : '';
            }");

                    var screenshotPath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "ontrac-login-failed.png");

                    await page.ScreenshotAsync(new()
                    {
                        Path = screenshotPath,
                        FullPage = true
                    });

                    return BadRequest(new
                    {
                        success = false,
                        message = "Login failed.",
                        loginErrorText,
                        currentUrl = page.Url,
                        screenshotPath
                    });
                }

                var reportUrl =
                    $"https://fastrac.ontrac.com/reportviewer/Report" +
                    $"?reportname=Business%20Report" +
                    $"&driverId={driverId}" +
                    $"&beginDate={beginDate}" +
                    $"&endDate={endDate}";

                await page.GotoAsync(reportUrl, new()
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 60000
                });

                await page.WaitForTimeoutAsync(5000);

                var reportScreenshot = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"business-report-{driverId}.png");

                await page.ScreenshotAsync(new()
                {
                    Path = reportScreenshot,
                    FullPage = true
                });

                return Ok(new
                {
                    success = true,
                    afterLoginUrl,
                    currentUrl = page.Url,
                    title = await page.TitleAsync(),
                    reportUrl,
                    screenshotPath = reportScreenshot
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        private static string ExtractPdfText(Stream stream)
        {
            stream.Position = 0;

            using var document = PdfDocument.Open(stream);
            var sb = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                var words = page.GetWords()
                    .Select(w => w.Text)
                    .Where(x => !string.IsNullOrWhiteSpace(x));

                sb.AppendLine(string.Join(" ", words));
            }

            return sb.ToString();
        }

        private static DateTime? ExtractDate(string text)
        {
            text = Regex.Replace(text ?? "", @"\s+", " ").Trim();

            var match = Regex.Match(
                text,
                @"Date\s+(\d{1,2}/\d{1,2}/\d{4})",
                RegexOptions.IgnoreCase
            );

            if (!match.Success)
            {
                match = Regex.Match(
                    text,
                    @"(\d{1,2}/\d{1,2}/\d{4})",
                    RegexOptions.IgnoreCase
                );
            }

            if (!match.Success)
                return null;

            return DateTime.TryParse(match.Groups[1].Value, out var date)
                ? date.Date
                : null;
        }

        private static string ExtractDriver(string text)
        {
            text = Regex.Replace(text ?? "", @"\s+", " ").Trim();

            var match = Regex.Match(
                text,
                @"Driver\s+([A-Z]{2,}[A-Z0-9]*\d+)",
                RegexOptions.IgnoreCase
            );

            if (match.Success)
                return match.Groups[1].Value.Trim().ToUpper();

            match = Regex.Match(
                text,
                @"\b(ST[A-Z]{2}\d{2})\b",
                RegexOptions.IgnoreCase
            );

            return match.Success
                ? match.Groups[1].Value.Trim().ToUpper()
                : "";
        }

        private static List<PdfPackageRow> ExtractPackageRows(string text)
        {
            var result = new List<PdfPackageRow>();

            text = Regex.Replace(text ?? "", @"\s+", " ").Trim();

            // Corta desde después del header de tabla
            var headerIndex = text.IndexOf("Stop Number", StringComparison.OrdinalIgnoreCase);
            if (headerIndex >= 0)
                text = text.Substring(headerIndex);

            // Busca cada paquete por patrón:
            // StopNumber + OrderId + Address hasta USA
            var matches = Regex.Matches(
                text,
                @"(?<stop>\d{1,3})\s+(?<order>(?:WP|ZX)\d+)\s+(?<address>.*?\bUSA\b)",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in matches)
            {
                var stopNumber = int.TryParse(match.Groups["stop"].Value, out var sn)
                    ? sn
                    : 0;

                var orderId = match.Groups["order"].Value.Trim();
                var address = match.Groups["address"].Value.Trim();

                if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(address))
                    continue;

                result.Add(new PdfPackageRow
                {
                    StopNumber = stopNumber,
                    OrderId = orderId,
                    Address = address,
                    Phone = "",
                    Notes = ""
                });
            }

            return result
                .GroupBy(x => x.OrderId.Trim().ToUpperInvariant())
                .Select(g => g.First())
                .OrderBy(x => x.StopNumber)
                .ToList();
        }

        private static string NormalizeAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return "";

            var value = address.Trim().ToUpperInvariant();

            value = Regex.Replace(value, @"\s+", " ");
            value = value.Replace(", USA", "");
            value = value.Replace(" USA", "");

            return value.Trim();
        }

        private static ParsedAddress ParseAddress(string rawAddress)
        {
            if (string.IsNullOrWhiteSpace(rawAddress))
                return new ParsedAddress();

            var address = rawAddress.Trim();

            address = Regex.Replace(address, @"\s+", " ");
            address = address.Replace(", USA", "", StringComparison.OrdinalIgnoreCase)
                             .Replace(" USA", "", StringComparison.OrdinalIgnoreCase)
                             .Trim();

            var match = Regex.Match(
                address,
                @"^(?<street>.*?),\s*(?<city>[^,]+),\s*(?<state>[A-Z]{2})\s*(?<zip>\d{5}(?:-\d{4})?)?",
                RegexOptions.IgnoreCase
            );

            if (!match.Success)
            {
                return new ParsedAddress
                {
                    Address = address
                };
            }

            return new ParsedAddress
            {
                Address = match.Groups["street"].Value.Trim(),
                City = match.Groups["city"].Value.Trim(),
                State = match.Groups["state"].Value.Trim().ToUpperInvariant(),
                ZipCode = match.Groups["zip"].Value.Trim()
            };
        }
        [Authorize]
        [HttpGet("user/{userId:int}")]
        public async Task<IActionResult> GetRoutesByUser(
    int userId,
    [FromQuery] DateTime? startDate = null,
    [FromQuery] DateTime? endDate = null)
        {
            var query = _context.Routes
                .AsNoTracking()
                .Include(r => r.Zone)
                .Include(r => r.User)
                .Where(r => r.UserId == userId);

            if (startDate.HasValue)
                query = query.Where(r => r.Date >= startDate.Value.Date);

            if (endDate.HasValue)
                query = query.Where(r => r.Date <= endDate.Value.Date);

            var routes = await query
                .OrderByDescending(r => r.Date)
                .Select(r => new
                {
                    r.Id,
                    r.Date,
                    r.RouteCode,
                    r.DeliveryStops,
                    r.Volumen,
                    r.Attempts,
                    r.CNL,
                    r.Los,
                    r.CustomerOnTime,
                    r.BranchOnTime,
                    r.PriceRoute,
                    r.PaymentType,
                    RouteStatus = r.routeStatus.ToString(),

                    Zone = r.Zone == null
                        ? null
                        : new
                        {
                            r.Zone.Id,
                            r.Zone.ZoneCode
                        }
                })
                .ToListAsync();

            return Ok(routes);
        }


    } 
}
internal static class ClaimsExtensions
{
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var raw =
            user.FindFirstValue(ClaimTypes.NameIdentifier) ??
            user.FindFirst("sub")?.Value ??
            user.FindFirst("id")?.Value ??
            user.FindFirst("userId")?.Value;

        return int.TryParse(raw, out var id) ? id : null;
    }

    public static bool HasAnyRole(this ClaimsPrincipal user, params string[] roles)
        => roles.Any(r => user.IsInRole(r));
}

public class PdfPackageRow
{
    public int StopNumber { get; set; }
    public string OrderId { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
}

public class ParsedAddress
{
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
}


public class RouteUserZoneDto
{
    public int Id { get; set; }
    public string IdentificationNumber { get; set; }
    public string UserName { get; set; }
    public string UserLastName { get; set; }
    public string Zone { get; set; }
}

public class RoutesDto
{
    public int Id { get; set; }
    public int ZoneId { get; set; }
    public int Volumen { get; set; }
    public int DeliveryStops { get; set; } = 0;
    public DateTime Date { get; set; }
    public double? PriceRoute   { get; set; }
    public PaymentType paymentType { get; set; }
    public Warehouse? Warehouse { get; set; }
    public int? WarehouseId { get; set; }

}

// ✅ DTO para recibir datos del frontend
public class RouteUpdateDto
{
    public int Id { get; set; }
    public int? ZoneId { get; set; }       // null = desasignar zona
    public int? CNL { get; set; }          // null permitido
    public int? UserId { get; set; }       // null = desasignar driver
    public string? RouteStatus { get; set; } // 'Available' | 'Assigned' | 'In Progress' | 'Future' | 'Completed'

    public string? PaymentType { get; set; }
    public decimal? PriceRoute { get; set; }
}

