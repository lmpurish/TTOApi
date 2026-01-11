   using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using TToApp.DTOs;
using TToApp.Model;


namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PackagesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ApiURL _apiUrl;
        private readonly EmailService _emailService;

        public PackagesController(ApplicationDbContext context, IOptions<ApiURL> apiURL, EmailService emailService)
        {
            _context = context;
            _apiUrl = apiURL.Value;
            _emailService = emailService;
        }

        // GET: api/Packages
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Packages>>> GetPackages()
        {
            return await _context.Packages.ToListAsync();
        }

        // GET: api/Packages/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Packages>> GetPackages(int id)
        {
            var packages = await _context.Packages.FindAsync(id);

            if (packages == null)
            {
                return NotFound();
            }

            return packages;
        }

        [HttpGet("packageByManager/{managerId}")]
        public async Task<ActionResult<List<PackageDto>>> GetPackagesByManager(int managerId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == managerId);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            List<int> rspIds;

            if (user.UserRole == global::User.Role.Manager)
            {
                var rsp = await _context.Users
                    .FirstOrDefaultAsync(u => u.WarehouseId == user.WarehouseId && u.UserRole == global::User.Role.Rsp);

                if (rsp == null)
                {
                    return NotFound(new { message = "RSP not found for this manager." });
                }

                if (!int.TryParse(rsp.IdentificationNumber, out int rspId))
                {
                    return BadRequest(new { message = "RSP identification number is not a valid number." });
                }

                rspIds = new List<int> { rspId };
            }
            else if (user.UserRole == global::User.Role.Admin || user.UserRole == global::User.Role.CompanyOwner)
            {
                var rspIdStrings = await _context.Users
                    .Where(u => u.UserRole ==  global::User.Role.Rsp)
                    .Select(u => u.IdentificationNumber)
                    .ToListAsync();

                 rspIds = rspIdStrings
                    .Select(idStr => int.TryParse(idStr, out int id) ? (int?)id : null)
                    .Where(id => id.HasValue)
                    .Select(id => id.Value)
                    .ToList();
            }
            else
            {
                return Forbid();
            }

            var packages = await _context.Packages
                .Include(p => p.Routes)
                    .ThenInclude(r => r.User)
                .Include(p => p.Routes)
                    .ThenInclude(r => r.Zone)
                .Where(p => rspIds.Contains ( (int)p.RSP))
                .ToListAsync();

            var packageDtos = packages.Select(p => new PackageDto
            {
                Id = p.Id,
                IncidentDate = p.IncidentDate,
                Tracking = p.Tracking,
                Address = p.Address,
                City = p.City,
                State = p.State,
                ZipCode = p.ZipCode,
                ReviewStatus = p.ReviewStatus.ToString(),
                Status = p.Status.ToString(),
                ScanLat = p.ScanLat,
                ScanLon = p.ScanLon,
                AddrLat = p.AddrLat,
                AddrLon = p.AddrLon,
                DayElapsed = p.DaysElapsed,
                Route = p.Routes == null ? null : new RouteDto
                {
                    Id = p.Routes.Id,
                    Zone = p.Routes.Zone == null ? null : new ZoneDto
                    {
                        Id = p.Routes.Zone.Id,
                        ZoneCode = p.Routes.Zone.ZoneCode,
                    },
                    User = p.Routes.User == null ? null : new UserDto
                    {
                        Id = p.Routes.User.Id,
                        Name = p.Routes.User.Name,
                        LastName = p.Routes.User.LastName,
                        WarehouseId = p.Routes.User.WarehouseId,
                    }
                },
                RSP = p.RSP,
               
            }).OrderByDescending(p => p.IncidentDate).ToList();

            return Ok(packageDtos);
        }




        // PUT: api/Packages/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutPackages(int id, Packages packages)
        {
            if (id != packages.Id)
            {
                return BadRequest();
            }

            _context.Entry(packages).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PackagesExists(id))
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

        // POST: api/Packages
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<PackageDto>> PostPackages(PackageDto packageDto)
        {
            if (packageDto == null)
                return BadRequest("Invalid data.");

            if (!Enum.TryParse<PackageStatus>(packageDto.Status, true, out var parsedStatus))
                return BadRequest($"Invalid status: {packageDto.Status}");

            try
            {
                // Paso 1: Obtener la ruta
                var routeId = packageDto.Route?.Id;

                var route = await _context.Routes
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == routeId);

                if (route == null || route.User == null)
                    return BadRequest("Route or route user not found.");

                var warehouseId = route.User.WarehouseId;

                // Paso 2: Buscar el RSP asignado a ese warehouse
                var rspUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.WarehouseId == warehouseId && u.UserRole == global::User.Role.Rsp);

                if (rspUser == null)
                    return BadRequest("No se encontró un RSP asignado a este almacén.");

                // Convertir el IdentificationNumber a int (asegúrate de que sea numérico siempre)
                if (!int.TryParse(rspUser.IdentificationNumber, out int rspId))
                    return BadRequest("The RSP Identification Number is not valid.");

                // Paso 3: Crear el paquete
                var package = new Packages
                {
                    Tracking = packageDto.Tracking ?? "",
                    Address = packageDto.Address ?? "",
                    City = packageDto.City ?? "",
                    State = packageDto.State ?? "",
                    ZipCode = packageDto.ZipCode ?? "",
                    IncidentDate = packageDto.IncidentDate ?? DateTime.UtcNow,
                    Status = parsedStatus,
                    ReviewStatus = ReviewStatus.Open,
                    ScanLat = packageDto.ScanLat,
                    ScanLon = packageDto.ScanLon,
                    AddrLat = packageDto.AddrLat,
                    AddrLon = packageDto.AddrLon,
                    RoutesId = packageDto.Route?.Id,
                    DaysElapsed = 0,
                    Distance = "0",
                    Brand = "Generic",
                    RSP = rspId,
                    Notified = false
                };
               
                _context.Packages.Add(package);
                await _context.SaveChangesAsync();
                if (package.Status == PackageStatus.CNL ) {

                    var placeholders = new Dictionary<string, string>
                    {
                        ["Name"] = route.User.Name,
                        ["Tracking"] = package.Tracking,
                        ["IncidentDate"] = package.IncidentDate.ToString("yyyy-MM-dd"),
                        ["Status"] = "CNL",
                        ["Address"] = package.Address ?? "-",
                        ["City"] = package.City ?? "-",
                        ["State"] = package.State ?? "-",
                        ["ZipCode"] = package.ZipCode ?? "-",
                        ["Distance"] = package.Distance ?? "-",
                        // profundiad: deep link a tu app o web
                        ["ActionUrl"] = $"https://www.ontrac.com/tracking/?number={package.Tracking}"
                    };
                    route.CNL += 1;
                   
                    var notification = new Notification
                    {
                        UserId = route.UserId.Value,
                        Title = "You have a CNL",
                        Message = "You have a new CNL whit tracking number " + package.Tracking,
                        Type = NotificationType.System,
                        IsRead = false

                    };
                    await _emailService.SendEmailAsync(
                            toEmail: route.User.Email,
                            subject: $"You have a CNL: {package.Tracking}",
                            "CnlNotification.cshtml",
                            placeholders: placeholders,
                            copy: false
);
                    _context.Notifications.Add(notification);
                    await _context.SaveChangesAsync();
                }
                packageDto.Id = package.Id;
                return Ok(new { message = "Package added successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al guardar el paquete: {ex}");
                return StatusCode(500, "Error interno del servidor.");
            }
        }


        [HttpPost("requestPackagesReview")]
        public async Task<IActionResult> RequestPackagesReview(PackageReviewDTO request)
        {
            // Mapear el DTO a la entidad real
            var evidence = new PackageReviewEvidence
            {
                PackageId = request.PackageId,
                ImageName = request.ImageName,
                ImageUrl = request.ImageUrl,
                UploadedBy = request.UploadedBy,
                Description = request.Description,
                UploadedAt = DateTime.UtcNow
            };

            // Guardar evidencia
            _context.PackageReviewEvidences.Add(evidence);
            await _context.SaveChangesAsync();

            // Obtener admins
            var admins = await _context.Users
                .Where(u => u.UserRole == global::User.Role.Admin && u.IsActive)
                .ToListAsync();

            // Obtener información del paquete
            var package = await _context.Packages
                .Where(p => p.Id == request.PackageId)
                .FirstOrDefaultAsync();

            if (package == null)
                return NotFound("Package not found.");

            // Enviar notificaciones
            var httpClient = new HttpClient();
            string notificationUrl = _apiUrl.NotificationApiUrl;

            foreach (var admin in admins)
            {
                var notification = new Notification
                {
                    UserId = admin.Id,
                    Message = $"📦 New review request for the package #{package.Tracking}",
                    Type = NotificationType.System,
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                };

                var content = new StringContent(JsonConvert.SerializeObject(notification), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(notificationUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Failed to notify admin {admin.Email}");
                }
            }

            return Ok("Request sent successfully!");
        }


        // DELETE: api/Packages/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePackages(int id)
        {
            var packages = await _context.Packages.FindAsync(id);

            if (packages == null)
            {
                return NotFound();
            }

            if(packages.Status == PackageStatus.CNL)
            {
                var route = await _context.Routes
            .FirstOrDefaultAsync(r => r.Id == packages.RoutesId);
                if(route.CNL > 0)
                    route.CNL -= 1;
            }
            _context.Packages.Remove(packages);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        
        private bool PackagesExists(int id)
        {
            return _context.Packages.Any(e => e.Id == id);
        }
    }
}
