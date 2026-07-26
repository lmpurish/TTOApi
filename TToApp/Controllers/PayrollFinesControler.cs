using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TToApp.DTOs;
using TToApp.Helpers;
using TToApp.Model;

namespace TToApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PayrollFinesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ApiURL _apiUrl;

    public PayrollFinesController(ApplicationDbContext context, IOptions<ApiURL> apiUrl)
    {
        _context = context;
        _apiUrl = apiUrl.Value;

    }

    // GET: api/PayrollFines?userId=1&packageId=2&type=Late&tracking=XXX&include=true
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PayrollFineDto>>> GetAll(
        [FromQuery] int? userId,
        [FromQuery] int? packageId,
        [FromQuery] string? type,
        [FromQuery] string? tracking,
        [FromQuery] bool include = false
    )
    {
        var q = _context.PayrollFines.AsNoTracking().AsQueryable();

        if (userId.HasValue) q = q.Where(x => x.UserId == userId.Value);
        if (packageId.HasValue) q = q.Where(x => x.PackageId == packageId.Value);
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(x => x.Type == type);
        if (!string.IsNullOrWhiteSpace(tracking)) q = q.Where(x => x.Tracking == tracking);

        if (include)
            q = q.Include(x => x.User).Include(x => x.Package);

        var data = await q
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PayrollFineDto
            {
                Id = x.Id,
                PackageId = x.PackageId,
                UserId = x.UserId,
                Tracking = x.Tracking,
                Amount = x.Amount,
                Type = x.Type,
                Description = x.Description,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                ChargedAt = x.ChargedAt,
                PayRunId = x.PayRunId,
                UserName = include ? (x.User.Name ?? x.User.Email) : null,
                PackageCode = include ? x.Package.Tracking : null
            })
            .ToListAsync();

        return Ok(data);
    }

    // GET: api/PayrollFines/5?include=true
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PayrollFineDto>> GetById(int id, [FromQuery] bool include = false)
    {
        var q = _context.PayrollFines.AsNoTracking().Where(x => x.Id == id);

        if (include)
            q = q.Include(x => x.User).Include(x => x.Package);

        var item = await q
            .Select(x => new PayrollFineDto
            {
                Id = x.Id,
                PackageId = x.PackageId,
                UserId = x.UserId,
                Tracking = x.Tracking,
                Amount = x.Amount,
                Type = x.Type,
                Description = x.Description,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                ChargedAt = x.ChargedAt,
                PayRunId = x.PayRunId,
                UserName = include ? (x.User.Name ?? x.User.Email) : null,
                PackageCode = include ? x.Package.Tracking : null
            })
            .FirstOrDefaultAsync();

        if (item is null) return NotFound($"PayrollFine {id} not found.");
        return Ok(item);
    }

    // POST: api/PayrollFines
    [HttpPost]
    public async Task<ActionResult<PayrollFineDto>> Create([FromBody] PayrollFineCreateDto dto)
    {
        var package = await _context.Packages
            .AsNoTracking()
            .Include(p => p.Routes)
            .FirstOrDefaultAsync(p => p.Tracking ==dto.Tracking);

        if (package is null) return BadRequest("Package not found.");
        
        //var userId = package.Routes?.UserId;
        int? finalUserIdNullable = package.Routes?.UserId;

        if (!finalUserIdNullable.HasValue)
            return BadRequest("Could not determine the UserId.");

        int userId = finalUserIdNullable.Value; // ✅ ya es int

        // Validaciones opcionales: existencia de FK
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
        if (!userExists) return BadRequest($"UserId {userId} not found.");

        var entity = new PayrollFine
        {
            UserId      = userId,
            PackageId   = package.Id,
            Tracking    = dto.Tracking,
            Amount      = dto.Amount,
            Type        = string.IsNullOrWhiteSpace(dto.Type) ? "Other" : dto.Type.Trim(),
            Description = dto.Description ?? "",
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = null
        };

        _context.PayrollFines.Add(entity);
        await _context.SaveChangesAsync();

        var result = new PayrollFineDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            PackageId = entity.PackageId,
            Tracking = entity.Tracking,
            Amount = entity.Amount,
            Type = entity.Type,
            Description = entity.Description,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };

        return CreatedAtAction(nameof(GetById), new { id = entity.Id, include = false }, result);
    }

    // PUT: api/PayrollFines/5  (update parcial estilo PATCH, pero con PUT)
    [HttpPut("{id:int}")]
    public async Task<ActionResult<PayrollFineDto>> Update(int id, [FromBody] PayrollFineUpdateDto dto)
    {
        var entity = await _context.PayrollFines.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound($"PayrollFine {id} not found.");

        if (dto.UserId.HasValue)
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == dto.UserId.Value);
            if (!userExists) return BadRequest($"UserId {dto.UserId.Value} not found.");
            entity.UserId = dto.UserId.Value;
        }

        if (dto.PackageId.HasValue)
        {
            var packageExists = await _context.Packages.AnyAsync(p => p.Id == dto.PackageId.Value);
            if (!packageExists) return BadRequest($"PackageId {dto.PackageId.Value} not found.");
            entity.PackageId = dto.PackageId.Value;
        }

        if (dto.Tracking is not null) entity.Tracking = dto.Tracking;
        if (dto.Amount.HasValue) entity.Amount = dto.Amount.Value;
        if (dto.Type is not null) entity.Type = dto.Type.Trim();
        if (dto.Description is not null) entity.Description = dto.Description;

        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var result = new PayrollFineDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            PackageId = entity.PackageId,
            Tracking = entity.Tracking,
            Amount = entity.Amount,
            Type = entity.Type,
            Description = entity.Description,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };

        return Ok(result);
    }

    // PUT: api/PayrollFines/5/toggle-active
    [HttpPut("{id:int}/toggle-active")]
    public async Task<ActionResult<PayrollFineDto>> ToggleActive(int id)
    {
        var entity = await _context.PayrollFines.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound($"PayrollFine {id} not found.");

        if (entity.ChargedAt != null)
            return BadRequest("Cannot modify a fine that has already been applied to a payroll.");

        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new PayrollFineDto
        {
            Id = entity.Id,
            PackageId = entity.PackageId,
            UserId = entity.UserId,
            Tracking = entity.Tracking,
            Amount = entity.Amount,
            Type = entity.Type,
            Description = entity.Description,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            ChargedAt = entity.ChargedAt,
            PayRunId = entity.PayRunId
        });
    }

    [HttpPost("import/details")]
    public async Task<ActionResult> ImportFromExcelDetails([FromForm] PayrollFineImportRequest request)
    {
        var file = request.File;
        if (file == null || file.Length == 0)
            return BadRequest("File not found.");

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;

        using var workbook = new XLWorkbook(stream);

        if (!workbook.Worksheets.Any(w => w.Name == "Details"))
            return BadRequest("Worksheet 'Details' does not exist.");

        var ws = workbook.Worksheet("Details");
        var used = ws.RangeUsed();

        if (used == null)
            return BadRequest("Worksheet 'Details' is empty.");

        var rows = used.RowsUsed().Skip(1); // skip headers

        var created = 0;
        var errors = new List<object>();

        foreach (var row in rows)
        {
            var rowNum = row.RowNumber();

            var tracking = row.Cell("A").GetString()?.Trim(); // Tracking Number
            var type = row.Cell("F").GetString()?.Trim();      // Claim Category

            decimal amount;
            var amountStr = row.Cell("B").GetString()?.Trim();

            if (!decimal.TryParse(amountStr, out amount) && !row.Cell(2).TryGetValue(out amount))
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, Error = "Invalid amount." });
                continue;
            }

            var description = $"Imported from Excel row {rowNum}";

            if (string.IsNullOrWhiteSpace(tracking))
            {
                errors.Add(new { Row = rowNum, Error = "Tracking is empty." });
                continue;
            }

            var package = await _context.Packages
                .AsNoTracking()
                .Include(p => p.Routes)
                .FirstOrDefaultAsync(p => p.Tracking == tracking);

            if (package is null)
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, Error = "Package not found." });
                continue;
            }

            int? userIdNullable = package.Routes?.UserId;

            if (!userIdNullable.HasValue)
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, Error = "UserId could not be determined from the route." });
                continue;
            }

            int userId = userIdNullable.Value;

            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);

            if (!userExists)
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, UserId = userId, Error = "UserId does not exist." });
                continue;
            }

            var exists = await _context.PayrollFines.AnyAsync(x =>
                x.Tracking == tracking &&
                x.Type == type);

            if (exists)
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, Error = "Duplicate PayrollFine detected." });
                continue;
            }

            var entity = new PayrollFine
            {
                UserId = userId,
                PackageId = package.Id,
                Tracking = tracking,
                Amount = amount,
                Type = type,
                Description = description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = null
            };

            _context.PayrollFines.Add(entity);
            created++;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            Created = created,
            Errors = errors.Count,
            ErrorRows = errors
        });
    }

    // POST: api/PayrollFines/import/warehouse/{warehouseId}
    // Warehouse.Company determina el formato y la estrategia de resolución de driver:
    //   "SwiftX"  → sheet "Penalty Detail": A=Tracking, B=Type, C=Amount, D=DriverName → match Users.Name+LastName
    //   "Uni Uni" → sheet "Penalty":        A=tno, B=driver_id, C=adj_amount(neg), E=type → match Users.IdentificationNumber
    //   "Speedx"  → sheet "Claims":         A=Tracking, F=PackageValue, type="Claim" → Tracking→Package→Route→UserId
    [HttpPost("import/warehouse/{warehouseId:int}")]
    public async Task<ActionResult> ImportByWarehouseFormat(int warehouseId, [FromForm] PayrollFineImportRequest request)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest("File not found.");

        var warehouse = await _context.Warehouses
            .AsNoTracking()
            .Select(w => new { w.Id, w.Company })
            .FirstOrDefaultAsync(w => w.Id == warehouseId);

        if (warehouse == null)
            return NotFound($"Warehouse {warehouseId} not found.");

        var format = warehouse.Company?.Trim().ToLowerInvariant() switch
        {
            "swiftx"  => "swift",
            "uni uni" => "uniuni",
            "speedx"  => "speedx",
            _         => null
        };

        if (format == null)
            return BadRequest($"Warehouse has Company='{warehouse.Company}' with no import format configured. Supported: SwiftX, Uni Uni, Speedx.");

        using var stream = new MemoryStream();
        await request.File.CopyToAsync(stream);
        stream.Position = 0;

        using var workbook = new XLWorkbook(stream);

        string sheetName = format switch
        {
            "swift"  => "Penalty Detail",
            "uniuni" => "Penalty",
            "speedx" => "Claims",
            _        => ""
        };

        var ws = workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase));

        if (ws == null)
            return BadRequest($"Worksheet '{sheetName}' not found in the file.");

        var used = ws.RangeUsed();
        if (used == null)
            return BadRequest("The worksheet is empty.");

        // --- parsear filas; driverKey = nombre (swift) | driver_id (uniuni) | null (speedx) ---
        var rowData = new List<(string tracking, decimal amount, string type, string? driverKey)>();
        foreach (var row in used.RowsUsed().Skip(1))
        {
            string tracking, type;
            decimal amount;
            string? driverKey = null;

            if (format == "swift")
            {
                tracking  = row.Cell(1).GetString().Trim();
                type      = row.Cell(2).GetString().Trim();
                
                amount    = row.Cell(3).TryGetValue<decimal>(out var a) ? Math.Abs(a) : 0;
                driverKey = row.Cell(4).GetString().Trim(); // Driver Name
            }
            else if (format == "uniuni")
            {
                tracking  = row.Cell(1).GetString().Trim();
                driverKey = row.Cell(2).GetString().Trim(); // driver_id → IdentificationNumber
                amount    = row.Cell(3).TryGetValue<decimal>(out var a) ? Math.Abs(a) : 0;
                type      = row.Cell(5).GetString().Trim();
            }
            else // speedx
            {
                tracking = row.Cell(1).GetString().Trim();
                amount   = row.Cell(6).TryGetValue<decimal>(out var a) ? Math.Abs(a) : 0;
                type     = row.Cell(5).GetString().Trim();
            }

            if (string.IsNullOrWhiteSpace(tracking) || amount <= 0) continue;
            if (string.IsNullOrWhiteSpace(type)) type = "Other";

            rowData.Add((tracking, amount, type, driverKey));
        }

        if (rowData.Count == 0)
            return BadRequest("No valid rows found in the file.");

        var trackings = rowData.Select(r => r.tracking).Distinct().ToList();

        // --- Speedx: necesita package para resolver UserId ---
        var packageByTracking = new Dictionary<string, Packages>();
        if (format == "speedx")
        {
            var packages = await _context.Packages
                .AsNoTracking()
                .Include(p => p.Routes)
                .Where(p => p.Tracking != null && trackings.Contains(p.Tracking))
                .ToListAsync();

            packageByTracking = packages
                .Where(p => p.Tracking != null)
                .GroupBy(p => p.Tracking)
                .ToDictionary(g => g.Key ?? "", g => g.First());
        }

        // --- Swift: driver por nombre; UniUni: driver por IdentificationNumber ---
        var userByName           = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var userByIdentification = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (format is "swift" or "uniuni")
        {
            var warehouseUsers = await _context.UserWarehouses
                .AsNoTracking()
                .Where(uw => uw.WarehouseId == warehouseId && uw.IsActive)
                .Select(uw => new
                {
                    uw.User.Id,
                    uw.User.Name,
                    uw.User.LastName,
                    uw.User.IdentificationNumber
                })
                .ToListAsync();

            if (format == "swift")
            {
                foreach (var u in warehouseUsers.Where(u => !string.IsNullOrWhiteSpace(u.Name)))
                {
                    var key = NameHelper.NormalizeDriverFullName($"{u.Name} {u.LastName}");
                    if (!userByName.TryGetValue(key, out var list))
                        userByName[key] = list = new List<int>();
                    list.Add(u.Id);
                }
            }
            else
            {
                foreach (var u in warehouseUsers.Where(u => !string.IsNullOrWhiteSpace(u.IdentificationNumber)))
                    userByIdentification.TryAdd(u.IdentificationNumber!.Trim(), u.Id);
            }
        }

        // --- deduplicación ---
        var existingKeys = (await _context.PayrollFines
            .AsNoTracking()
            .Where(f => f.Tracking != null && trackings.Contains(f.Tracking))
            .Select(f => new { f.Tracking, f.Type, f.UserId })
            .ToListAsync())
            .Select(f => $"{f.Tracking}|{f.Type}|{f.UserId}")
            .ToHashSet();

        // --- procesar filas ---
        var created = 0;
        var skipped = 0;
        var errors  = new List<object>();

        foreach (var (tracking, amount, type, driverKey) in rowData)
        {
            int? userId;
            int? packageId = null;

            if (format == "swift")
            {
                if (!NameHelper.TryFindByName(userByName, driverKey ?? "", out var uid))
                {
                    errors.Add(new { tracking, driverName = driverKey, reason = "Driver not found in the warehouse by name." });
                    continue;
                }
                userId = uid;
            }
            else if (format == "uniuni")
            {
                var idStr = (driverKey ?? "").Trim();
                if (!userByIdentification.TryGetValue(idStr, out var uid))
                {
                    errors.Add(new { tracking, driverId = driverKey, reason = "Driver not found in the warehouse by identification number." });
                    continue;
                }
                userId = uid;
            }
            else // speedx
            {
                if (!packageByTracking.TryGetValue(tracking, out var pkg))
                {
                    errors.Add(new { tracking, reason = "Package not found in the system." });
                    continue;
                }
                userId = pkg.Routes?.UserId;
                if (userId == null)
                {
                    errors.Add(new { tracking, reason = "Package has no route/driver assigned." });
                    continue;
                }
                packageId = pkg.Id;
            }

            var key = $"{tracking}|{type}|{userId}";
            if (!existingKeys.Add(key))
            {
                skipped++;
                continue;
            }

            _context.PayrollFines.Add(new PayrollFine
            {
                UserId    = userId.Value,
                PackageId = packageId,
                Tracking  = tracking,
                Amount    = amount,
                Type      = type,
                CreatedAt = DateTime.UtcNow
            });
            created++;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            Created      = created,
            Skipped      = skipped,
            Errors       = errors.Count,
            ErrorDetails = errors
        });
    }
}
