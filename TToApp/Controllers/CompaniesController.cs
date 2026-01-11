using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TToApp.Model;
using TToApp.Services.Auth;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CompaniesController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        public CompaniesController(ApplicationDbContext context, IWebHostEnvironment env, IJwtService jwtService)
        {
            _context = context;
            _env = env;
            _jwtService = jwtService;
        }

        // GET: api/Companies
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Company>>> GetCompanies()
        {
            return await _context.Companies.ToListAsync();
        }



        // PUT: api/Companies/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754


        // POST: api/Companies
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost("create")]
        [Authorize]
        public async Task<IActionResult> CreateCompany([FromForm] CompanyCreateDto dto)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var ownerId))
                return Unauthorized("Usuario no autenticado");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == ownerId);
            if (user is null)
                return BadRequest(new { Message = "User not found" });

            if (user.CompanyId != null)
                return Conflict(new { Message = "El usuario ya tiene una compañía asociada." });

            if (dto.Logo is null || dto.Logo.Length == 0)
                return BadRequest("El archivo del logo es obligatorio");

            // 📂 Guardar logo
            var webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var uploadPath = Path.Combine(webRoot, "uploads", "Companylogos");
            Directory.CreateDirectory(uploadPath);

            var ext = Path.GetExtension(dto.Logo.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadPath, fileName);
            var relativeUrl = $"/uploads/Companylogos/{fileName}";

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await dto.Logo.CopyToAsync(stream);
            }

            // 🗄 Crear compañía
            var company = new Company
            {
                Name = dto.CompanyName,
                Email = dto.CompanyEmail,
                Address = dto.Address,
                PhoneNumber = dto.PhoneNumber,
                LogoUrl = relativeUrl,
                OwnerId = ownerId
            };

            _context.Companies.Add(company);
            await _context.SaveChangesAsync();

            var companyDto = new
            {
                company.Id,
                company.Name,
                company.Email,
                company.Address,
                company.PhoneNumber,
                company.LogoUrl,
                company.OwnerId
            };
            // 🔗 Vincular usuario
            user.CompanyId = company.Id;
            user.IsFirstLogin = false;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            // 🔑 Generar token actualizado con CompanyLogo incluido
            var token = _jwtService.CreateJwtToken(user, company);

            return Ok(new
            {
                message = "✅ Compañía creada exitosamente",
                company = companyDto,
                token
            });
        }


        // DELETE: api/Companies/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCompany(int id)
        {
            var company = await _context.Companies.FindAsync(id);
            if (company == null)
            {
                return NotFound();
            }

            _context.Companies.Remove(company);
            await _context.SaveChangesAsync();

            return NoContent();
        }

      
        [HttpPost("create-stripe-customer")]
        [Authorize]
        public async Task<IActionResult> CreateStripeCustomer()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            var user = await _context.Users.FindAsync(userId);

            if (user == null) return NotFound();

            var customerService = new CustomerService();
            var customer = await customerService.CreateAsync(new CustomerCreateOptions
            {
                Email = user.Email,
                Name = $"{user.Name} {user.LastName}",
                Phone = user.Profile?.PhoneNumber ?? null
            });

            user.StripeCustomerId = customer.Id;
            await _context.SaveChangesAsync();

            return Ok(new { customerId = customer.Id });
        }

    

        [HttpPost("attach-payment-method")]
        [Authorize]
        public async Task<IActionResult> AttachPaymentMethod([FromBody] AttachPaymentDto dto)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            var user = await _context.Users.FindAsync(userId);

            if (user == null || string.IsNullOrEmpty(user.StripeCustomerId))
                return BadRequest("El usuario no tiene un cliente Stripe asociado");

            var paymentMethodService = new PaymentMethodService();
            await paymentMethodService.AttachAsync(dto.PaymentMethodId, new PaymentMethodAttachOptions
            {
                Customer = user.StripeCustomerId
            });

            var customerService = new CustomerService();
            await customerService.UpdateAsync(user.StripeCustomerId, new CustomerUpdateOptions
            {
                InvoiceSettings = new CustomerInvoiceSettingsOptions
                {
                    DefaultPaymentMethod = dto.PaymentMethodId
                }
            });

            user.StripePaymentMethodId = dto.PaymentMethodId;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Método de pago asociado",
                paymentMethodId = dto.PaymentMethodId
            });
        }
        [HttpPost("update-logo")]
        [Authorize(Roles = "CompanyOwner")]
        public async Task<IActionResult> UpdateLogo(IFormFile logo)
        {
            // ✅ Validar archivo
            if (logo == null || logo.Length == 0)
                return BadRequest("No file uploaded");

            // ✅ Validar usuario
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out var id))
                return Unauthorized("Invalid user");

            var company = await _context.Companies.FirstOrDefaultAsync(c => c.OwnerId == id);
            if (company == null)
                return NotFound("Company not found");

            // ✅ Guardar archivo
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/CompanyLogos");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(logo.FileName)}";
            var filePath = Path.Combine(folder, fileName);

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                    await logo.CopyToAsync(stream);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error saving file: {ex.Message}");
            }

            // ✅ Actualizar base de datos
            company.LogoUrl = $"/uploads/CompanyLogos/{fileName}";
            await _context.SaveChangesAsync();

            // ✅ Generar nuevo token
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return Unauthorized("User not found");

            var token = _jwtService.CreateJwtToken(user, company);

            return Ok(new { logoUrl = company.LogoUrl, token });
        }


        [HttpGet("statsCompany/{id}/simple")]
        public async Task<IActionResult> GetCompanySimpleStats(int id)
        {
            // Verifica que exista la compañía
            var companyExists = await _context.Companies
                .AsNoTracking()
                .AnyAsync(c => c.Id == id);
            if (!companyExists) return NotFound(new { Message = "Company not found." });

            // Query base: rutas pertenecientes a la compañía (Route -> Zone -> Warehouse -> CompanyId)
            var routesQ = _context.Routes
                .AsNoTracking()
                .Where(r => r.Zone != null &&
                            r.Zone.Warehouse != null &&
                            r.Zone.Warehouse.CompanyId == id);

            // Totales desde Routes (se ejecuta en SQL)
            var aggregates = await routesQ
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalVolumePackages = g.Sum(r => (int?)r.Volumen) ?? 0,
                    TotalStops = g.Sum(r => (int?)r.DeliveryStops) ?? 0
                })
                .FirstOrDefaultAsync() ?? new { TotalVolumePackages = 0, TotalStops = 0 };

            // Cantidad de almacenes de la compañía
            var warehousesCount = await _context.Warehouses
                .AsNoTracking()
                .CountAsync(w => w.CompanyId == id);

            // Conductores activos asignados a rutas de esta compañía (distinct)
            var activeDrivers = await _context.Users
                     .AsNoTracking()
                     .CountAsync(u => u.CompanyId == id && u.UserRole == global::User.Role.Driver);

            return Ok(new
            {
                TotalVolumePackages = aggregates.TotalVolumePackages,
                TotalStops = aggregates.TotalStops,
                Warehouses = warehousesCount,
                ActiveDrivers = activeDrivers
            });
        }


        public class CompanyCreateDto
        {
            public string CompanyName { get; set; }
            public string CompanyEmail { get; set; }
            public string Address { get; set; }
            public string PhoneNumber { get; set; }
            public IFormFile Logo { get; set; }
        }

        public class AttachPaymentDto
        {
            public string PaymentMethodId { get; set; }
        }

        private bool CompanyExists(int id)
        {
            return _context.Companies.Any(e => e.Id == id);
        }
    }
}
