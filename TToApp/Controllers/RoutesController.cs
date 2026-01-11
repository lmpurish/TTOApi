using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.Blazor;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Xml.Linq;
using TToApp.DTOs;
using TToApp.Model;




namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoutesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly INotificationService _notificationService;

        public RoutesController(ApplicationDbContext context, EmailService emailService, INotificationService notificationService)
        {
            _context = context;
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _notificationService = notificationService;
        }

        // GET: api/Routes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Routes>>> GetRoutes()
        {
            return await _context.Routes.ToListAsync();
        }
        [HttpGet("by-date")]
        public async Task<ActionResult<IEnumerable<object>>> GetRoutesByDate([FromQuery] DateTime date, [FromQuery] int? warehouseId = null)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized("Invalid user ID.");
            }

            int resolvedWarehouseId;

            if (userRole == "Manager")
            {
                var managerWarehouseId = await _context.Users
                    .Where(u => u.Id == userId)
                    .Select(u => u.WarehouseId)
                    .FirstOrDefaultAsync();

                if (!managerWarehouseId.HasValue)
                {
                    return NotFound("Manager not found or does not have an assigned warehouse.");
                }

                resolvedWarehouseId = managerWarehouseId.Value;
            }
            else
            {
                if (!warehouseId.HasValue)
                {
                    return BadRequest("Warehouse ID is required for non-manager users.");
                }

                resolvedWarehouseId = warehouseId.Value;
            }

            // Obtener las rutas filtrando por warehouseId y fecha
            var routesRaw = await _context.Routes
    .AsNoTracking()
    .Where(r =>
       // Sin usuario => validar por zona
       (r.Zone != null && r.Zone.IdWarehouse == resolvedWarehouseId)
    ||
    // O si el usuario pertenece al almacén, que pase
    (r.UserId != null && r.User.WarehouseId == resolvedWarehouseId)
    )
    // Fecha segura (evita .Date en LINQ to SQL)
    .Where(r => r.Date >= date.Date && r.Date < date.Date.AddDays(1))
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


            // Transformación fuera del LINQ (en memoria)
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
                routeStatus = r.routeStatus != null ? GetReadableStatus(r.routeStatus.Value) : "no status",
                r.PaymentType,
                r.PriceRoute,
                r.User,
                r.Zone
            });

            return Ok(routes);
        }


        [Authorize]
        [HttpPut("assign-routes")]
        public async Task<IActionResult> AssignRoutes([FromBody] List<RouteUpdateDto> routeUpdates)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out _))
                return Unauthorized(new { message = "Invalid or missing user." });

            if (routeUpdates == null || routeUpdates.Count == 0)
                return BadRequest(new { message = "No routes provided." });

            var ids = routeUpdates.Select(x => x.Id).Distinct().ToList();
            var routes = await _context.Routes.Where(r => ids.Contains(r.Id)).ToListAsync();
            if (routes.Count == 0)
                return NotFound(new { message = "No matching routes found." });

            var updatesById = routeUpdates.ToDictionary(x => x.Id);
            var updated = new List<object>();

            foreach (var route in routes)
            {
                var u = updatesById[route.Id];
                bool changed = false;

                // 1) APLICA PRIMERO EL USERID (permite desasignar en esta misma petición)
                if (route.UserId != u.UserId)
                {
                    route.UserId = u.UserId; // admite null
                    changed = true;
                }

                // 2) VALIDA/APLICA STATUS EN FUNCIÓN DEL USERID RESULTANTE
                var requestedStatus = ParseRouteStatus(u.RouteStatus); // usa tu helper existente
                if (requestedStatus.HasValue)
                {
                    // Con driver: solo Assigned, InProgress o Completed
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

                    // Sin driver: NO permitir Assigned/InProgress/Completed
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

                // 3) RESTO DE CAMPOS
                if (route.ZoneId != u.ZoneId) { route.ZoneId = u.ZoneId; changed = true; }
                if (route.CNL != u.CNL) { route.CNL = (int)u.CNL; changed = true; }

                if (changed)
                {
                    updated.Add(new
                    {
                        route.Id,
                        route.ZoneId,
                        route.CNL,
                        route.UserId,
                        routeStatus = route.routeStatus.ToString()
                    });
                }
            }

            await _context.SaveChangesAsync();

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

            // ⚠️ RouteStatus es enum -> columna int en DB
            var available = (int)RouteStatus.Available;
            var assigned = (int)RouteStatus.Assigned;

            // Toma la ruta solo si sigue disponible y sin usuario
            var rows = await _context.Routes
          .Where(r => r.Id == id && r.UserId == null && (r.routeStatus == RouteStatus.Available || r.routeStatus == RouteStatus.Future))
          .ExecuteUpdateAsync(upd => upd
          .SetProperty(r => r.UserId, userId)
          .SetProperty(r => r.routeStatus, RouteStatus.Assigned));

            if (rows == 1)
            {
                await tx.CommitAsync();

                var route = await _context.Routes
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == id);

                return Ok(new
                {
                    message = "Route successfully claimed.",
                    route = new { route?.Id, route?.routeStatus, route?.UserId }
                });
            }

            await tx.RollbackAsync();

            // Mensajes claros
            var exists = await _context.Routes.AnyAsync(r => r.Id == id);
            if (!exists) return NotFound(new { message = "Route not found." });

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

            // Solo desasigna si: es la misma ruta, el mismo usuario y estado EXACTO = Assigned
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

                return Ok(new
                {
                    message = "Route was unassigned successfully.",
                    route = new { route?.Id, route?.routeStatus, route?.UserId }
                });
            }

            await tx.RollbackAsync();

            // Explica por qué no se pudo
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


        [HttpPost("upload/{warehouseId}")]
        public async Task<IActionResult> UploadXmlFile(IFormFile file, int warehouseId)
        {
            if (file == null || file.Length == 0 || Path.GetExtension(file.FileName).ToLower() != ".xml")
                return BadRequest(new { message = "Debe subir un archivo XML válido con extensión .xml." });

            try
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                stream.Position = 0;

                XDocument xmlDoc = XDocument.Load(stream);
                XNamespace ns = xmlDoc.Root?.GetDefaultNamespace() ?? "";

                // 🔍 Obtener la fecha del reporte
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
               

                // 🔍 Obtener `Branch On Time` del XML
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

                /*     Console.WriteLine($"📌 Branch On Time obtenido del XML: {branchOnTimeForRSP}");
                     Console.WriteLine($"📌 Branch On Time obtenido del XML: {LosForRSP}");
                */

                var notifiedPackages = new List<(string Tracking,string Status ,int DaysElapsed)>();

                // 🔍 Obtener los `IdentificationNumber` únicos desde el XML
                var spValues = details
                    .Select(d => d.Attribute("SP__")?.Value?.Trim())
                    .Where(sp => !string.IsNullOrEmpty(sp))
                    .Distinct()
                    .ToList();

                if (!spValues.Any())
                    return BadRequest(new { message = "No se encontraron IdentificationNumber en el XML." });

                // 🔍 Obtener los conductores de la base de datos basados en `IdentificationNumber` y `WarehouseId` recibido
                var users = await _context.Users
                    .Where(u => spValues.Contains(u.IdentificationNumber) && u.WarehouseId == warehouseId)
                    .ToListAsync();

                var rsp = await _context.Users
                    .Where(u => u.UserRole ==  global::User.Role.Rsp && u.WarehouseId == warehouseId)
                    .FirstOrDefaultAsync();

                // 🔍 Crear diccionario `(IdentificationNumber, WarehouseId) -> UserId`
                var userIds = users
                    .GroupBy(u => new { u.IdentificationNumber, u.WarehouseId })
                    .ToDictionary(g => (g.Key.IdentificationNumber, g.Key.WarehouseId), g => g.First().Id);

                if (!userIds.Any())
                    return BadRequest(new { message = "No se encontraron coincidencias en la base de datos para los conductores del XML en este Warehouse." });

                // 🔍 Obtener el `UserId` del RSP del `WarehouseId`
                var rspUsers = users
                    .Where(u => u.UserRole == global::User.Role.Rsp)
                    .ToDictionary(u => u.WarehouseId, u => u.Id);

                var manager = await _context.Users
                    .FirstOrDefaultAsync(u => u.WarehouseId == warehouseId && u.UserRole == global::User.Role.Manager);


                // 🔍 Obtener los UserId que ya tienen rutas en la fecha
                var existingRoutes = await _context.Routes
                    .Where(r => r.Date.Date == reportDate.Date)
                    .Select(r => r.UserId)
                    .ToHashSetAsync();

                var routesToSave = new List<Routes>();
                var Packages = new List<Packages>();

                // 🔄 Procesar cada detalle y agregar rutas a la lista
                foreach (var detail in details)
                {
                    string spValue = detail.Attribute("SP__")?.Value?.Trim() ?? "0";
                    int volumen = SafeParseInt(detail.Attribute("Volume3")?.Value);
                    int attempts = SafeParseInt(detail.Attribute("Incomplete_D5")?.Value);

                    // ✅ Verificar si el usuario pertenece al `WarehouseId` proporcionado
                    if (!userIds.TryGetValue((spValue, warehouseId), out int userId))
                        continue;

                    // ✅ Evitar duplicados: Si ya tiene una ruta en la fecha, no la creamos
                    if (existingRoutes.Contains(userId))
                        continue;

                    // 🔍 Obtener valores del XML
                    double los = volumen > 0 ? SafeParseDouble(detail.Attribute("LOS3")?.Value) * 100 : 0;
                    int cnlValue = SafeParseInt(detail.Attribute("CNL3")?.Value);
                    int customerOnTimeNumerator = volumen > 0 ? SafeParseInt(detail.Attribute("Customer_On_Time_Numerator")?.Value) : 0;
                    int customerOnTimeDenominator = volumen > 0 ? SafeParseInt(detail.Attribute("Customer_On_Time_Denominator")?.Value) : 1;
                    double customerOnTime = (customerOnTimeDenominator > 0) ? (double)customerOnTimeNumerator / customerOnTimeDenominator * 100 : 0;

                    var route = new Routes
                    {
                        Date = reportDate,
                        DeliveryStops = volumen > 0 ? SafeParseInt(detail.Attribute("Delivery_Stops3")?.Value) : 0,
                        Volumen = volumen,
                        Los = los,
                        CustomerOnTime = customerOnTime,
                        UserId = userId,
                        routeStatus = RouteStatus.Completed,
                        Attempts = attempts,
                        CNL = cnlValue,
                        BranchOnTime = 100 // Se asignará más adelante si es un RSP
                    };

                    routesToSave.Add(route);
                    
                }

                // 🔍 Asignar `BranchOnTime` al RSP dentro del `WarehouseId`
                foreach (var route in routesToSave)
                {
                    if (rspUsers.TryGetValue(warehouseId, out int rspUserId))
                    {
                        if (route.UserId == rspUserId)
                        {
                            route.BranchOnTime = branchOnTimeForRSP;
                            route.Los = LosForRSP;
                            Console.WriteLine($"✅ Asignando BranchOnTime: {branchOnTimeForRSP} al RSP con UserId: {rspUserId}");
                        }
                    }
                }

                // 🔍 Guardar las rutas si hay alguna para insertar
                if (routesToSave.Any())
                {
                    _context.Routes.AddRange(routesToSave);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    return BadRequest(new { message = "No se encontraron nuevas rutas para insertar en la base de datos." });
                }

                if (losBeforeCutoffDetails.Count == 0) {
                    Console.Write("⛔ No se encontraron RD, se omite la inserción de paquetes.");
                }
                else
                {
                    foreach (var detail in losBeforeCutoffDetails)
                    {
                        string tracking = detail.Attribute("tracking4")?.Value?.Trim();
                        string address = detail.Attribute("Delivery_Address4")?.Value?.Trim();
                        string city = detail.Attribute("Delviery_City4")?.Value?.Trim(); // typo confirmado
                        string state = detail.Attribute("Delivery_State4")?.Value?.Trim();
                        string zip = detail.Attribute("Delivery_Zip4")?.Value?.Trim();
                        int rsp1 = int.TryParse(rsp.IdentificationNumber, out var result) ? result : 0;
                        var attr = detail.Attribute("Driver4");

                  /*      if (attr != null && int.TryParse(attr.Value.Trim(), out int result))
                        {
                            rsp = result;
                        }
                  */
                        if (string.IsNullOrWhiteSpace(tracking))
                        {
                            Console.WriteLine("⚠️ Paquete ignorado: tracking vacío");
                            continue;
                        }

                        // 🔍 Buscar si el paquete ya existe
                        var existingPackage = await _context.Packages.FirstOrDefaultAsync(p => p.Tracking == tracking);

                        if (existingPackage != null)
                        {
                            if (existingPackage.Status == PackageStatus.RD)
                            {
                                existingPackage.DaysElapsed += 1;
                                existingPackage.IncidentDate = reportDate; // actualiza fecha si es necesario
                                _context.Packages.Update(existingPackage); // asegúrate de marcarlo para actualización
                                await _context.SaveChangesAsync(); // ✅ guardarlo en la base de datos
                                Console.WriteLine($"🔁 Paquete ya existente con estado RD. Incrementando DaysElapsed: {tracking}");
                            }
                            else
                            {
                                Console.WriteLine($"⚠️ Paquete ya existe con estado diferente ({existingPackage.Status}), ignorado: {tracking}");
                            }

                            continue;
                        }

                        // ✅ Insertar nuevo paquete
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

                        Console.WriteLine($"✅ Agregado paquete nuevo: {tracking}");
                    }
                  
                }
                if (Cnls.Count == 0)
                {
                    Console.Write("⛔ No se encontraron CNLs, se omite la inserción de paquetes.");
                }
                else
                {
                    var identificationToUserId = users.ToDictionary(u => u.IdentificationNumber, u => u.Id);

                    // Obtener rutas existentes para la fecha
                    var routeDictionary = await _context.Routes
                        .Where(r => r.Date.Date == reportDate.Date)
                        .ToDictionaryAsync(r => r.UserId, r => r.Id);


                    var existingTrackings = await _context.Packages
                        .Where(p => p.Status == PackageStatus.CNL)
                        .Select(p => p.Tracking)
                        .ToListAsync();

                   
                    if (Cnls != null || !Cnls.Any())
                    {
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
                            {
                                Console.WriteLine("⚠️ Paquete CNL ignorado por falta de datos");
                                continue;
                            }

                            if (!identificationToUserId.TryGetValue(driverIdentification, out int userId))
                            {
                                Console.WriteLine($"⚠️ No se encontró UserId para Driver5={driverIdentification}");
                                continue;
                            }

                            if (!routeDictionary.TryGetValue(userId, out int routeId))
                            {
                                Console.WriteLine($"⚠️ No se encontró RouteId para UserId={userId} (Driver5={driverIdentification}) en fecha {reportDate:yyyy-MM-dd}");
                                continue;
                            }

                            if (existingTrackings.Contains(tracking))
                            {
                                Console.WriteLine($"⚠️ Tracking duplicado: {tracking} ya existe");
                                continue;
                            }

                            Console.WriteLine($"➕ Intentando agregar paquete: {tracking}, RouteId={routeId}, DriverId={driverIdentification}");

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

                    // ✅ Guardar todos los nuevos de una vez
                }
                if (IncompleteDay2.Count == 0) {
                    Console.Write("⛔ No se encontraron Incomplete Day 2, se omite la inserción de paquetes.");
                }
                else
                {
                    
                    var existingTrackings = await _context.Packages
                        .Where(p => p.Tracking != null)
                        .Select(p => p.Tracking.Trim().ToUpper())
                        .ToListAsync();

                    if (IncompleteDay2 != null || !IncompleteDay2.Any())
                    {
                        foreach (var detail in IncompleteDay2)
                        {
                            int rsp1 = int.TryParse(rsp.IdentificationNumber, out var result) ? result : 0;
                            string tracking = detail.Attribute("tracking3")?.Value?.Trim();
                            string driverIdentification = detail.Attribute("Driver3")?.Value?.Trim();
                            string address = detail.Attribute("Delivery_Address3")?.Value?.Trim();
                            string city = detail.Attribute("Delviery_City3")?.Value?.Trim();
                            string state = detail.Attribute("Delivery_State3")?.Value?.Trim();
                            string zip = detail.Attribute("Delivery_Zip3")?.Value?.Trim();
                            string CurrentStatuscode1 = detail.Attribute("CurrentStatuscode1")?.Value?.Trim();

                            if (existingTrackings.Contains(tracking))
                            {

                                if (new[] { "CO", "NH", "OD", "WA", "ED", "UG","HW" }.Contains(CurrentStatuscode1))
                                {
                                    var existingPackage = await _context.Packages
                                        .FirstOrDefaultAsync(p => p.Tracking.Trim().ToUpper() == tracking);

                                    if (existingPackage != null && Enum.TryParse<PackageStatus>(CurrentStatuscode1, out var parsedStatus1))
                                    {
                                        existingPackage.Status = parsedStatus1;
                                        existingPackage.DaysElapsed += 1;
                                        existingPackage.IncidentDate = reportDate;
                                    }
                                    string title = "📦 Overdue Package Alert";
                                    string message = $"The package with tracking number {existingPackage.Tracking} has been open for more than 1 day. Please follow up.";
                                    await _notificationService.NotifyAsync(
                                        userId: manager.Id,
                                        title: title,
                                        message: message,
                                        type: NotificationType.Success,
                                        url: "",
                                        source: "Tracking System"
                                    );
                                    notifiedPackages.Add((existingPackage.Tracking,existingPackage.Status.ToString(),existingPackage.DaysElapsed));
                                }
                                Console.WriteLine($"⚠️ Tracking duplicado: {tracking} ya existe");
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
                                    Status = parsedStatus, // Usa el valor parseado del XML
                                    DaysElapsed = 1,
                                    RSP = rsp1
                                });
                            }

                        }
                        await _context.SaveChangesAsync();

                    }

                }


                if (Packages.Count > 0)
                {
                    _context.Packages.AddRange(Packages);
                    await _context.SaveChangesAsync();
                }

                var adminEmails = _context.Users
                    .Where(u => u.UserRole.Value == global::User.Role.Admin && !string.IsNullOrEmpty(u.Email))
                    .Select(u => u.Email)
                    .ToList();

                var warehouse = GetWarehouseCity(warehouseId);

                // Construir tabla HTML de paquetes
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

                // Preparar placeholders
                var placeholders = new Dictionary<string, string>
                {
                    { "warehouse", warehouse },
                    { "date", DateTime.Now.AddDays(-1).ToString("MMMM dd, yyyy", new System.Globalization.CultureInfo("en-US")) },
                    { "packageList", tableHtml.ToString() }
                };
                await _emailService.SendEmailAsync(
                        toEmail: manager.Email,
                        subject: "Information Loaded!",
                        "ConfirmUploadXml.cshtml",
                        placeholders: placeholders,
                        copy: false
                    );
                // Enviar a cada admin
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




                return Ok(new { message = $"{routesToSave.Count} registros guardados en Routes, incluyendo el RSP." });
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
        [HttpPost]
        public async Task<IActionResult> PostRoutes(RoutesDto routesDto)
        {
            if (routesDto == null)
                return BadRequest("Datos inválidos.");

            try
            {
                // Crear la ruta
                var routes = new Routes
                {
                    Date= routesDto.Date,
                    Volumen = routesDto.Volumen,
                    DeliveryStops = (int) routesDto.DeliveryStops,
                    ZoneId = routesDto.ZoneId,
                    routeStatus = RouteStatus.Created,
                    PriceRoute = routesDto.PriceRoute,
                    PaymentType = routesDto.paymentType 
                   
                };

                _context.Routes.Add(routes);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Route added successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al guardar la ruta: {ex}");
                return StatusCode(500, "Error interno del servidor.");
            }
        }

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

        [Authorize]
        [HttpPost("route-parcel-info")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportRouteParcelInfo(
    [FromForm] ImportRouteParcelInfoRequest req,
    CancellationToken ct)
        {
            if (req.File == null || req.File.Length == 0)
                return BadRequest(new { Message = "Archivo requerido." });

            var warehouseId = req.WarehouseId;

            // 1) Leer excel
            using var ms = new MemoryStream();
            await req.File.CopyToAsync(ms, ct);
            ms.Position = 0;

            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.First(); // normalmente "result"

            // 2) Headers -> índice (AQUÍ es donde nace headerMap)
            var headerRow = ws.Row(1);
            var headerMap = headerRow.CellsUsed()
                .ToDictionary(
                    c => c.GetString().Trim(),
                    c => c.Address.ColumnNumber,
                    StringComparer.OrdinalIgnoreCase);

            // Helpers que dependen de headerMap (DESPUÉS de headerMap)
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

                // Si viene numérico
                if (cell.DataType == XLDataType.Number)
                    return Convert.ToDecimal(cell.GetDouble());

                // Si viene string
                var s = cell.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(s)) return null;

                // soporte "1,23" o "1.23"
                s = s.Replace(",", ".");

                return decimal.TryParse(
                    s,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var val
                ) ? val : null;
            }

            // 3) Leer filas válidas
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var rawRows = new List<RouteParcelRow>();

            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);

                var tracking = S(row, "TrackingNo");
                var routeCode = S(row, "Route");
                var planDate = D(row, "planDeliveryDate");

                if (string.IsNullOrWhiteSpace(tracking) ||
                    string.IsNullOrWhiteSpace(routeCode) ||
                    planDate == null)
                    continue;

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
                    FinalStatus = S(row, "FinalStatus"),

                    // ✅ Peso (NO weightType)
                    Weight = Dec(row, "Weight")
                });
            }

            if (rawRows.Count == 0)
                return BadRequest(new { Message = "No hay filas válidas (TrackingNo/Route/planDeliveryDate)." });

            var minDate = rawRows.Min(x => x.Date);
            var maxDate = rawRows.Max(x => x.Date);

            // 4) Precargar usuarios (drivers) y construir mapa por nombre normalizado
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

            var filteredUsers = users.Where(u => u.WarehouseId == warehouseId).ToList();

            var userMap = filteredUsers
                .GroupBy(u => NormName($"{u.Name} {u.LastName}"))
                .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList(), StringComparer.OrdinalIgnoreCase);

            // 5) Agrupar por ruta (RouteCode + Date)
            var groups = rawRows.GroupBy(x => new { x.RouteCode, x.Date });

            // 6) Precargar rutas existentes en rango
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

                // ✅ VOLUMEN = paquetes (trackings únicos) por ruta
                var volume = g
                    .Select(x => x.Tracking)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                // ✅ STOPS = tu lógica (NO la cambio): StopKey con BuildStopKey(x)
                var stopGroups = g
                    .Select(x => new { x.Tracking, StopKey = BuildStopKey(x) })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Tracking) && !string.IsNullOrWhiteSpace(x.StopKey))
                    .GroupBy(x => x.StopKey, StringComparer.OrdinalIgnoreCase);

                var stops = stopGroups.Count();

                // (Opcional: solo para métricas, NO cambia tu stop)
                var multiPackageStops = stopGroups.Count(sg =>
                    sg.Select(z => z.Tracking).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

                var extraPackagesInMultiStops = stopGroups.Sum(sg =>
                {
                    var c = sg.Select(z => z.Tracking).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    return c > 1 ? (c - 1) : 0;
                });

                var cnl = g.Count(x => string.Equals(x.FinalStatus, "CNL", StringComparison.OrdinalIgnoreCase));

                // DriverName (toma el primero no vacío dentro del grupo)
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
                        driverNotFound.Add(new
                        {
                            Date = date,
                            RouteCode = routeCode,
                            DriverName = driverRaw,
                            Normalized = driverKey
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

                var route = FindRoute(routeCode, date);

                if (route == null)
                {
                    route = new Routes
                    {
                        WarehouseId = warehouseId,

                        Date = date,
                        RouteCode = routeCode,

                        DeliveryStops = stops,
                        Volumen = volume, // ✅ volumen correcto
                        CNL = cnl,

                        Los = 0,
                        CustomerOnTime = 0,
                        BranchOnTime = 0,

                        Attempts = 0,
                        PaymentType = PaymentType.PerStop,
                        routeStatus = driverId.HasValue ? RouteStatus.Completed: RouteStatus.Pending,
                        UserId = driverId
                    };

                    _context.Routes.Add(route);
                    existingRoutes.Add(route);
                    createdRoutes++;

                    if (driverId.HasValue) driverAssigned++;
                }
                else
                {
                    route.DeliveryStops = stops;
                    route.Volumen = volume; // ✅ NO “stops”
                    route.CNL = cnl;

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

                // Si quieres devolver métricas opcionales por ruta, aquí podrías guardarlas
                // (por ahora NO toco tu modelo Routes)
                _ = multiPackageStops;
                _ = extraPackagesInMultiStops;
            }

            await _context.SaveChangesAsync(ct);

            // 7) Crear Packages (link con RoutesId)
            var routeLookup = existingRoutes
                .Where(r => r.WarehouseId == warehouseId && r.RouteCode != null)
                .ToDictionary(r => (r.RouteCode!, r.Date.Date), r => r.Id);

            var trackings = rawRows.Select(x => x.Tracking)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingTrackings = await _context.Packages
                .Where(p => trackings.Contains(p.Tracking))
                .Select(p => p.Tracking)
                .ToListAsync(ct);

            var trackingSet = existingTrackings.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var packagesAdded = 0;

            foreach (var x in rawRows)
            {
                if (!routeLookup.TryGetValue((x.RouteCode, x.Date), out var routeId))
                    continue;

                if (trackingSet.Contains(x.Tracking))
                    continue;

                var fullAddress = string.IsNullOrWhiteSpace(x.Unit) ? x.Address : $"{x.Address} #{x.Unit}";

                _context.Packages.Add(new Packages
                {
                    RoutesId = routeId,
                    Tracking = x.Tracking,
                    Address = fullAddress,
                    City = x.City,
                    State = x.State,
                    ZipCode = x.Zip,
                    IncidentDate = x.Date,

                    // ✅ Peso guardado (NO weightType)
                    Weight = x.Weight,

                    Status = PackageStatus.RD,
                    DaysElapsed = 0,
                    Notified = false,
                    ReviewStatus = ReviewStatus.Open
                });

                packagesAdded++;
            }

            await _context.SaveChangesAsync(ct);

            return Ok(new
            {
                Message = "Import OK",
                WarehouseId = warehouseId,
                DateRange = new { minDate, maxDate },
                RowsRead = rawRows.Count,
                RoutesCreated = createdRoutes,
                RoutesUpdated = updatedRoutes,
                PackagesAdded = packagesAdded,

                DriverAssignedRoutes = driverAssigned,
                DriverNotFound = driverNotFound.Take(50),
                DriverAmbiguous = driverAmbiguous.Take(50)
            });
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
            public string? FinalStatus { get; set; }
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
                        r.routeStatus == RouteStatus.Loading ? "Loading":
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

}

// ✅ DTO para recibir datos del frontend
public class RouteUpdateDto
{
    public int Id { get; set; }
    public int? ZoneId { get; set; }       // null = desasignar zona
    public int? CNL { get; set; }          // null permitido
    public int? UserId { get; set; }       // null = desasignar driver
    public string? RouteStatus { get; set; } // 'Available' | 'Assigned' | 'In Progress' | 'Future' | 'Completed'
}

