
using Microsoft.EntityFrameworkCore;
using TToApp.DTOs;
using TToApp.Model;

namespace TToApp.Services.Vehicle
{
    public interface IVehicleService
    {
        Task<List<RentalVehicle>> GetVehiclesAsync(int companyId, int? metroId, string? status);
        Task<RentalVehicle?> GetVehicleByIdAsync(int id);
        Task<(bool Success, string Message, RentalVehicle? Vehicle)> CreateVehicleAsync(CreateVehicleDto dto);
        Task<(bool Success, string Message, RentalVehicle? Vehicle)> UpdateVehicleAsync(int id, UpdateVehicleDto dto);
        Task<(bool Success, string Message)> UpdateVehicleStatusAsync(int id, string status);
        Task<(bool Success, string Message)> ArchiveVehicleAsync(int id);
    }

    public class VehicleService : IVehicleService
    {
        private readonly ApplicationDbContext _context;

        private static readonly string[] AllowedStatuses =
        {
            "Draft",
            "Available",
            "MaintenanceHold",
            "Disabled"
        };

        public VehicleService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<object>> GetVehiclesAsync(int? companyId, int? metroId, string? status)
        {
            var query = _context.RentalVehicles
                .Include(v => v.Company)
                .Include(v => v.Metro)
                .Include(v => v.Images)
                .Where(v => v.IsActive)
                .AsQueryable();

            if (companyId.HasValue)
                query = query.Where(v => v.CompanyId == companyId.Value);

            if (metroId.HasValue)
                query = query.Where(v => v.MetroId == metroId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(v => v.Status == status);

            return await query
                .OrderByDescending(v => v.CreatedAt)
                .Select(v => new
                {
                    v.Id,
                    v.CompanyId,
                    Company = v.Company != null ? v.Company.Name : null,
                    v.MetroId,
                    Metro = v.Metro != null ? v.Metro.City : null,
                    v.DisplayName,
                    v.StockNumber,
                    v.Year,
                    v.Make,
                    v.Model,
                    v.Trim,
                    v.Color,
                    v.Transmission,
                    v.FuelType,
                    v.SeatingCapacity,
                    v.DailyPrice,
                    v.WeeklyPrice,
                    v.DepositAmount,
                    v.Status,
                    v.MainImageUrl,
                    v.FacilityLocation,
                    v.GpsInstalled,
                    v.DashCamInstalled,
                    ImagesCount = v.Images.Count,
                    v.CreatedAt,
                    v.UpdatedAt
                })
                .Cast<object>()
                .ToListAsync();
        }

        public async Task<RentalVehicle?> GetVehicleByIdAsync(int id)
        {
            return await _context.RentalVehicles
                .Include(v => v.Company)
                .Include(v => v.Metro)
                .Include(v => v.Images.OrderBy(i => i.SortOrder))
                .FirstOrDefaultAsync(v => v.Id == id && v.IsActive);
        }

        public async Task<(bool Success, string Message, RentalVehicle? Vehicle)> CreateVehicleAsync(CreateVehicleDto dto)
        {
            var companyExists = await _context.Companies.AnyAsync(c => c.Id == dto.CompanyId);
            if (!companyExists)
                return (false, "Company not found.", null);

            var metroExists = await _context.Metro.AnyAsync(m => m.Id == dto.MetroId);
            if (!metroExists)
                return (false, "Metro not found.", null);

            if (dto.DailyPrice < 0 || dto.WeeklyPrice < 0 || dto.DepositAmount < 0)
                return (false, "Prices and deposit cannot be negative.", null);

            var status = string.IsNullOrWhiteSpace(dto.Status) ? "Draft" : dto.Status.Trim();
            if (!AllowedStatuses.Contains(status))
                return (false, "Invalid vehicle status.", null);

            var vehicle = new RentalVehicle
            {
                CompanyId = dto.CompanyId,
                MetroId = dto.MetroId,
                DisplayName = dto.DisplayName.Trim(),
                StockNumber = dto.StockNumber?.Trim(),
                Year = dto.Year,
                Make = dto.Make?.Trim() ?? "",
                Model = dto.Model?.Trim() ?? "",
                Trim = dto.Trim?.Trim(),
                Color = dto.Color?.Trim(),
                Transmission = dto.Transmission?.Trim(),
                FuelType = dto.FuelType?.Trim(),
                SeatingCapacity = dto.SeatingCapacity,
                TrunkNotes = dto.TrunkNotes?.Trim(),
                DailyPrice = dto.DailyPrice,
                WeeklyPrice = dto.WeeklyPrice,
                DepositAmount = dto.DepositAmount,
                Status = status,
                Vin = dto.Vin?.Trim(),
                Plate = dto.Plate?.Trim(),
                FacilityLocation = dto.FacilityLocation?.Trim(),
                Notes = dto.Notes?.Trim(),
                GpsInstalled = dto.GpsInstalled,
                DashCamInstalled = dto.DashCamInstalled,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.RentalVehicles.Add(vehicle);
            await _context.SaveChangesAsync();

            return (true, "Vehicle created successfully.", vehicle);
        }

        public async Task<(bool Success, string Message, RentalVehicle? Vehicle)> UpdateVehicleAsync(int id, UpdateVehicleDto dto)
        {
            var vehicle = await _context.RentalVehicles.FirstOrDefaultAsync(v => v.Id == id && v.IsActive);
            if (vehicle == null)
                return (false, "Vehicle not found.", null);

            var companyExists = await _context.Companies.AnyAsync(c => c.Id == dto.CompanyId);
            if (!companyExists)
                return (false, "Company not found.", null);

            var metroExists = await _context.Metro.AnyAsync(m => m.Id == dto.MetroId);
            if (!metroExists)
                return (false, "Metro not found.", null);

            if (dto.DailyPrice < 0 || dto.WeeklyPrice < 0 || dto.DepositAmount < 0)
                return (false, "Prices and deposit cannot be negative.", null);

            var status = string.IsNullOrWhiteSpace(dto.Status) ? vehicle.Status : dto.Status.Trim();
            if (!AllowedStatuses.Contains(status))
                return (false, "Invalid vehicle status.", null);

            vehicle.CompanyId = dto.CompanyId;
            vehicle.MetroId = dto.MetroId;
            vehicle.DisplayName = dto.DisplayName.Trim();
            vehicle.StockNumber = dto.StockNumber?.Trim();
            vehicle.Year = dto.Year;
            vehicle.Make = dto.Make?.Trim() ?? "";
            vehicle.Model = dto.Model?.Trim() ?? "";
            vehicle.Trim = dto.Trim?.Trim();
            vehicle.Color = dto.Color?.Trim();
            vehicle.Transmission = dto.Transmission?.Trim();
            vehicle.FuelType = dto.FuelType?.Trim();
            vehicle.SeatingCapacity = dto.SeatingCapacity;
            vehicle.TrunkNotes = dto.TrunkNotes?.Trim();
            vehicle.DailyPrice = dto.DailyPrice;
            vehicle.WeeklyPrice = dto.WeeklyPrice;
            vehicle.DepositAmount = dto.DepositAmount;
            vehicle.Status = status;
            vehicle.Vin = dto.Vin?.Trim();
            vehicle.Plate = dto.Plate?.Trim();
            vehicle.FacilityLocation = dto.FacilityLocation?.Trim();
            vehicle.Notes = dto.Notes?.Trim();
            vehicle.GpsInstalled = dto.GpsInstalled;
            vehicle.DashCamInstalled = dto.DashCamInstalled;
            vehicle.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return (true, "Vehicle updated successfully.", vehicle);
        }

        public async Task<(bool Success, string Message)> UpdateVehicleStatusAsync(int id, string status)
        {
            var vehicle = await _context.RentalVehicles.FirstOrDefaultAsync(v => v.Id == id && v.IsActive);
            if (vehicle == null)
                return (false, "Vehicle not found.");

            status = status?.Trim() ?? "";
            if (!AllowedStatuses.Contains(status))
                return (false, "Invalid vehicle status.");

            vehicle.Status = status;
            vehicle.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return (true, "Vehicle status updated successfully.");
        }
        public async Task<List<RentalVehicle>> GetVehiclesAsync(int companyId, int? metroId, string? status)
        {
            var query = _context.RentalVehicles
                .Include(v => v.Company)
                .Include(v => v.Metro)
                .Include(v => v.Images)
                .Where(v => v.CompanyId == companyId && v.IsActive)
                .AsQueryable();

            if (metroId.HasValue)
                query = query.Where(v => v.MetroId == metroId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(v => v.Status == status);

            return await query
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();
        }

        public async Task<(bool Success, string Message)> ArchiveVehicleAsync(int id)
        {
            var vehicle = await _context.RentalVehicles.FirstOrDefaultAsync(v => v.Id == id && v.IsActive);
            if (vehicle == null)
                return (false, "Vehicle not found.");

            vehicle.IsActive = false;
            vehicle.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return (true, "Vehicle archived successfully.");
        }
    }
}
