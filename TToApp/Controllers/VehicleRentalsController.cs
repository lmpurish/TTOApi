using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VehicleRentalsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public VehicleRentalsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CreateRental([FromBody] CreateVehicleRentalDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (dto.EndDate < dto.StartDate)
                return BadRequest("La fecha final no puede ser menor que la fecha inicial.");

            var vehicle = await _context.RentalVehicles
                .FirstOrDefaultAsync(v => v.Id == dto.RentalVehicleId);

            if (vehicle is null)
                return NotFound("El vehículo no existe.");

            if (!vehicle.IsActive || vehicle.Status == "Disabled" || vehicle.Status == "MaintenanceHold")
                return BadRequest("El vehículo no está disponible para renta.");

            var renter = await _context.RentalRenters
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == dto.RentalRenterId);

            if (renter is null)
                return NotFound("El renter no existe.");

            if (!renter.IsActive)
                return BadRequest("El renter está inactivo.");

            var hasOverlap = await _context.VehicleRentals
                .AnyAsync(r =>
                    r.RentalVehicleId == dto.RentalVehicleId &&
                    (r.Status == "Reserved" || r.Status == "Active") &&
                    dto.StartDate <= r.EndDate &&
                    dto.EndDate >= r.StartDate);

            if (hasOverlap)
                return BadRequest("El vehículo ya está reservado o rentado en ese rango de fechas.");

            var totalDays = dto.EndDate.DayNumber - dto.StartDate.DayNumber + 1;
            var totalAmount = totalDays * vehicle.DailyPrice;

            var rental = new VehicleRental
            {
                RentalVehicleId = dto.RentalVehicleId,
                RentalRenterId = dto.RentalRenterId,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                DailyPrice = vehicle.DailyPrice,
                WeeklyPrice = vehicle.WeeklyPrice,
                DepositAmount = vehicle.DepositAmount,
                TotalAmount = totalAmount,
                Notes = dto.Notes,
                Status = "Reserved",
                StartMileage = vehicle.Mileage,
                EndMileage = null,
                CreatedAt = DateTime.UtcNow
            };

            _context.VehicleRentals.Add(rental);

            vehicle.Status = "Reserved";
            vehicle.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetRentalById), new { id = rental.Id }, rental);
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetRentalById(long id)
        {
            var rental = await _context.VehicleRentals
                .AsNoTracking()
                .Include(x => x.RentalVehicle)
                .Include(x => x.RentalRenter)
                    .ThenInclude(x => x.User)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (rental is null)
                return NotFound();

            return Ok(rental);
        }

        [HttpPut ("complete") ]
        public async Task<IActionResult> CompleteRental([FromBody] CloseVehicleRentalDto dto)
        {
            var rental = await _context.VehicleRentals
                .Include(r => r.RentalVehicle)
                .FirstOrDefaultAsync(r => r.Id ==  dto.RentalId);

            if (rental is null)
                return NotFound("Bad parameter");

            if (rental.Status == "Completed")
                return BadRequest("The rental is already completed.");

            if (rental.Status == "Cancelled")
                return BadRequest("The rental is cancelled and cannot be completed.");

            if (dto.EndMileage < rental.StartMileage)
                return BadRequest("the end mileage cannot be less than the start mileage.");

            rental.Status       = "Completed";
            rental.EndMileage   = dto.EndMileage;
            rental.EndDate      = DateOnly.FromDateTime(dto.EndDate);     
            rental.UpdatedAt = DateTime.UtcNow;

            if (rental.RentalVehicle is not null)
            {
                rental.RentalVehicle.Status = "Available";
                rental.RentalVehicle.UpdatedAt = DateTime.UtcNow;
                rental.RentalVehicle.Mileage   = rental.EndMileage ?? rental.RentalVehicle.Mileage;

            }

            await _context.SaveChangesAsync();

            return Ok(new
                {
                    rental.Id,
                    rental.StartMileage,
                    rental.EndMileage,
                    MilesUsed = rental.EndMileage - rental.StartMileage,
                    VehicleMileage = rental.RentalVehicle.Mileage
                });
        }
    }
}