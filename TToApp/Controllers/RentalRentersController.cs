using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RentalRentersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public RentalRentersController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateRentalRenterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var exists = await _context.RentalRenters
                .AnyAsync(x => x.Email == dto.Email);

            if (exists)
                return BadRequest("Ya existe un renter con ese email.");

            var renter = new RentalRenter
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                PhoneNumber = dto.PhoneNumber,
                DriverLicenseNumber = dto.DriverLicenseNumber,
                DriverLicenseExpiration = dto.DriverLicenseExpiration,
                IdentificationNumber = dto.IdentificationNumber,
                Address = dto.Address,
                City = dto.City,
                State = dto.State,
                ZipCode = dto.ZipCode,
                EmergencyContactName = dto.EmergencyContactName,
                EmergencyContactPhone = dto.EmergencyContactPhone,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _context.RentalRenters.Add(renter);
            await _context.SaveChangesAsync();

            return Ok(renter);
        }

        [HttpPost("from-user/{userId:int}")]
        public async Task<IActionResult> CreateFromUser(int userId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(x => x.Profile)
                .FirstOrDefaultAsync(x => x.Id == userId);

            if (user is null)
                return NotFound("El usuario no existe.");

            if (string.IsNullOrWhiteSpace(user.Email))
                return BadRequest("El usuario no tiene email.");

            var renter = await _context.RentalRenters
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (renter is null)
            {
                renter = new RentalRenter
                {
                    UserId = user.Id,
                    CreatedAt = DateTime.UtcNow
                };

                _context.RentalRenters.Add(renter);
            }

            renter.FirstName = user.Name?.Trim() ?? "";
            renter.LastName = user.LastName?.Trim() ?? "";
            renter.Email = user.Email.Trim();
            renter.IdentificationNumber = user.IdentificationNumber?.Trim();

            renter.PhoneNumber = user.Profile?.PhoneNumber?.Trim();
            renter.DriverLicenseNumber = user.Profile?.DriverLicenseNumber?.Trim();
            renter.DriverLicenseExpiration = user.Profile?.ExpDriverLicense;

            renter.Address = user.Profile?.Address?.Trim();
            renter.City = user.Profile?.City?.Trim();
            renter.State = user.Profile?.State?.Trim();
            renter.ZipCode = user.Profile?.ZipCode?.Trim();

            renter.IsActive = user.IsActive;
            renter.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(renter);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var renter = await _context.RentalRenters
                .AsNoTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (renter is null)
                return NotFound();

            return Ok(renter);
        }
    }
}