using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TToApp.Model;

namespace TToApp.Controllers
{
    [Route("api/users/{userId:int}/warehouses")]
    [ApiController]
    [Authorize]
    public class UserWarehouseController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public UserWarehouseController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET /api/users/{userId}/warehouses
        [HttpGet]
        public async Task<IActionResult> GetUserWarehouses(int userId, CancellationToken ct)
        {
            var user = await _db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == userId, ct);

            if (!user)
                return NotFound(new { message = $"User {userId} not found." });

            var warehouses = await _db.UserWarehouses
                .AsNoTracking()
                .Include(uw => uw.Warehouse)
                .Where(uw => uw.UserId == userId)
                .OrderByDescending(uw => uw.IsPrimary)
                .ThenBy(uw => uw.Warehouse.City)
                .Select(uw => new
                {
                    uw.Id,
                    uw.IsPrimary,
                    uw.IsActive,
                    startDate = uw.StartDate.HasValue ? uw.StartDate.Value.ToString("yyyy-MM-dd") : null,
                    endDate = uw.EndDate.HasValue ? uw.EndDate.Value.ToString("yyyy-MM-dd") : null,
                    uw.CreatedAt,
                    warehouse = new
                    {
                        uw.Warehouse.Id,
                        uw.Warehouse.Name,
                        uw.Warehouse.City,
                        uw.Warehouse.State,
                        uw.Warehouse.Address,
                        uw.Warehouse.FacilityCode
                    }
                })
                .ToListAsync(ct);

            return Ok(warehouses);
        }

        // POST /api/users/{userId}/warehouses
        [HttpPost]
        public async Task<IActionResult> AssignWarehouse(
            int userId,
            [FromBody] AssignWarehouseRequest req,
            CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null)
                return NotFound(new { message = $"User {userId} not found." });

            var warehouse = await _db.Warehouses.AnyAsync(w => w.Id == req.WarehouseId, ct);
            if (!warehouse)
                return NotFound(new { message = $"Warehouse {req.WarehouseId} not found." });

            var existing = await _db.UserWarehouses
                .FirstOrDefaultAsync(uw => uw.UserId == userId && uw.WarehouseId == req.WarehouseId, ct);

            if (existing != null)
            {
                // Reactivate if it was inactive
                existing.IsActive = true;
                existing.EndDate = null;
                if (req.IsPrimary) existing.IsPrimary = true;
            }
            else
            {
                _db.UserWarehouses.Add(new UserWarehouse
                {
                    UserId = userId,
                    WarehouseId = req.WarehouseId,
                    IsPrimary = req.IsPrimary,
                    IsActive = true,
                    StartDate = req.StartDate,
                    CreatedBy = req.CreatedBy
                });
            }

            // Only one warehouse can be primary
            if (req.IsPrimary)
            {
                var otherPrimaries = await _db.UserWarehouses
                    .Where(uw => uw.UserId == userId && uw.WarehouseId != req.WarehouseId && uw.IsPrimary)
                    .ToListAsync(ct);

                foreach (var other in otherPrimaries)
                    other.IsPrimary = false;

                user.WarehouseId = req.WarehouseId;
            }
            // If user has no primary yet, make this one primary automatically
            else if (user.WarehouseId == null)
            {
                var hasAnyPrimary = await _db.UserWarehouses
                    .AnyAsync(uw => uw.UserId == userId && uw.IsPrimary, ct);

                if (!hasAnyPrimary)
                {
                    if (existing != null) existing.IsPrimary = true;
                    user.WarehouseId = req.WarehouseId;
                }
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Warehouse assigned successfully." });
        }

        // PUT /api/users/{userId}/warehouses/{warehouseId}/set-primary
        [HttpPut("{warehouseId:int}/set-primary")]
        public async Task<IActionResult> SetPrimary(int userId, int warehouseId, CancellationToken ct)
        {
            var entry = await _db.UserWarehouses
                .FirstOrDefaultAsync(uw => uw.UserId == userId && uw.WarehouseId == warehouseId, ct);

            if (entry == null)
                return NotFound(new { message = "This user is not assigned to that warehouse." });

            // Clear other primaries
            var others = await _db.UserWarehouses
                .Where(uw => uw.UserId == userId && uw.WarehouseId != warehouseId && uw.IsPrimary)
                .ToListAsync(ct);

            foreach (var other in others)
                other.IsPrimary = false;

            entry.IsPrimary = true;
            entry.IsActive = true;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user != null) user.WarehouseId = warehouseId;

            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Primary warehouse updated." });
        }

        // DELETE /api/users/{userId}/warehouses/{warehouseId}
        [HttpDelete("{warehouseId:int}")]
        public async Task<IActionResult> RemoveWarehouse(
            int userId,
            int warehouseId,
            [FromQuery] DateOnly? endDate,
            CancellationToken ct)
        {
            var entry = await _db.UserWarehouses
                .FirstOrDefaultAsync(uw => uw.UserId == userId && uw.WarehouseId == warehouseId, ct);

            if (entry == null)
                return NotFound(new { message = "This user is not assigned to that warehouse." });

            if (entry.IsPrimary)
                return BadRequest(new { message = "Cannot remove the primary warehouse. Set another warehouse as primary first." });

            entry.IsActive = false;
            entry.EndDate = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Warehouse removed from user." });
        }
    }

    public class AssignWarehouseRequest
    {
        public int WarehouseId { get; set; }
        public bool IsPrimary { get; set; } = false;
        public DateOnly? StartDate { get; set; }
        public int? CreatedBy { get; set; }
    }
}
