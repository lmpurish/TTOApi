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
using Newtonsoft.Json;
using TToApp.DTOs;
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
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
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
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                UserName = include ? (x.User.Name ?? x.User.Email) : null, 
                PackageCode = include ? x.Package.Tracking : null         
            })
            .FirstOrDefaultAsync();

        if (item is null) return NotFound($"PayrollFine {id} no existe.");
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

        if (package is null) return BadRequest("Package no existe.");
        
        //var userId = package.Routes?.UserId;
        int? finalUserIdNullable = package.Routes?.UserId;

        if (!finalUserIdNullable.HasValue)
            return BadRequest("No se pudo determinar el UserId.");

        int userId = finalUserIdNullable.Value; // ✅ ya es int

        // Validaciones opcionales: existencia de FK
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
        if (!userExists) return BadRequest($"UserId {userId} no existe.");

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
        if (entity is null) return NotFound($"PayrollFine {id} no existe.");

        if (dto.UserId.HasValue)
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == dto.UserId.Value);
            if (!userExists) return BadRequest($"UserId {dto.UserId.Value} no existe.");
            entity.UserId = dto.UserId.Value;
        }

        if (dto.PackageId.HasValue)
        {
            var packageExists = await _context.Packages.AnyAsync(p => p.Id == dto.PackageId.Value);
            if (!packageExists) return BadRequest($"PackageId {dto.PackageId.Value} no existe.");
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

    [HttpPost("import/details")]
    public async Task<ActionResult> ImportFromExcelDetails([FromForm] PayrollFineImportRequest request)
    {
         var file = request.File;
        if (file == null || file.Length == 0)
            return BadRequest("File no found");

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;

        using var workbook = new XLWorkbook(stream);

        if (!workbook.Worksheets.Any(w => w.Name == "Details"))
            return BadRequest("La hoja 'Details' no existe.");

        var ws = workbook.Worksheet("Details");
        var used = ws.RangeUsed();

        if (used == null)
            return BadRequest("La hoja 'Details' está vacía.");

        var rows = used.RowsUsed().Skip(1); // saltar headers

        var created = 0;
        var errors = new List<object>();

        foreach (var row in rows)
        {
            var rowNum = row.RowNumber();

            var tracking = row.Cell("A").GetString()?.Trim(); // Tracking Numbe                 
            var type = row.Cell("F").GetString()?.Trim();;         // Claim Category
            decimal amount;
            var amountStr = row.Cell("C").GetString()?.Trim();
            if (!decimal.TryParse(amountStr, out amount) && !row.Cell(2).TryGetValue(out amount))
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, Error = "Amount inválido" });
                continue;
            }
            var description = $"Imported from Excel row {rowNum}";

            if (string.IsNullOrWhiteSpace(tracking))
            {
                errors.Add(new { Row = rowNum, Error = "Tracking vacío." });
                continue;
            }
            // 🔍 Buscar package por Tracking
            var package = await _context.Packages
                .AsNoTracking()
                .Include(p => p.Routes)
                .FirstOrDefaultAsync(p => p.Tracking == tracking);


            if (package is null)
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, Error = "Package no existe." });
                continue;
            }

            int? userIdNullable = package.Routes?.UserId;
            if (!userIdNullable.HasValue)
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, Error = "No se pudo determinar UserId desde Route." });
                continue;
            }

            int userId = userIdNullable.Value;

            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, UserId = userId, Error = "UserId no existe." });
                continue;
            }

            // 🚫 Evitar duplicados (Tracking + Type)
            var exists = await _context.PayrollFines.AnyAsync(x =>
                x.Tracking == tracking &&
                x.Type == type);

            if (exists)
            {
                errors.Add(new { Row = rowNum, Tracking = tracking, Error = "PayrollFine duplicado." });
                continue;
            }

            var entity = new PayrollFine
            {
                UserId      = userId,
                PackageId   = package.Id,
                Tracking    = tracking,
                Amount      = amount,
                Type        = type,
                Description = description,
                CreatedAt   = DateTime.UtcNow,
                UpdatedAt   = null
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
}
