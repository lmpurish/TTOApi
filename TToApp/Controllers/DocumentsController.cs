using iText.IO.Image;
using iText.IO.Source;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TToApp.Model;

namespace TToApp.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/company-docs")]
    public class CompanyDocsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<CompanyDocsController> _logger;

        public CompanyDocsController(
            ApplicationDbContext db,
            IWebHostEnvironment env,
            ILogger<CompanyDocsController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        // ----------------- Helpers -----------------
        private string PublicUrl(string rel) =>
            $"{Request.Scheme}://{Request.Host}{Request.PathBase}/{rel.Replace("\\", "/")}";

        private static bool IsPdf(IFormFile f) =>
            f.ContentType?.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) == true ||
            System.IO.Path.GetExtension(f.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        private bool TryMapPublicToPhysical(string urlOrRel, out string absPath)
        {
            var webRoot = _env.WebRootPath ?? System.IO.Path.Combine(_env.ContentRootPath, "wwwroot");
            var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/";

            if (urlOrRel.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
                urlOrRel = urlOrRel.Substring(baseUrl.Length);

            var rel = urlOrRel.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString());
            absPath = System.IO.Path.Combine(webRoot, rel);
            return System.IO.File.Exists(absPath);
        }
        static readonly JsonSerializerOptions FieldsOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        static FieldsJsonSetup Done = Init();
        static FieldsJsonSetup Init()
        {
            FieldsOptions.Converters.Add(new JsonStringEnumConverter()); // <-- enums como string
            return new FieldsJsonSetup();
        }
        sealed class FieldsJsonSetup { } // sólo para forzar Init una vez

        private (int pageCount, List<Rectangle> pageBoxes) ReadPdfInfo(Stream pdfStream)
        {
            using var ms = new MemoryStream();
            pdfStream.CopyTo(ms);
            ms.Position = 0;

            using var rdr = new PdfReader(new RandomAccessSourceFactory().CreateSource(ms.ToArray()), new ReaderProperties());
            using var pdf = new PdfDocument(rdr);
            int pages = pdf.GetNumberOfPages();
            var boxes = new List<Rectangle>(pages);
            for (int i = 1; i <= pages; i++)
            {
                var page = pdf.GetPage(i);
                boxes.Add(page.GetPageSize()); // Origen (0,0) abajo-izquierda
            }
            return (pages, boxes);
        }

        private (int pageCount, List<Rectangle> pageBoxes) ReadPdfInfo(byte[] pdfBytes)
        {
            using var rdr = new PdfReader(new RandomAccessSourceFactory().CreateSource(pdfBytes), new ReaderProperties());
            using var pdf = new PdfDocument(rdr);
            int pages = pdf.GetNumberOfPages();
            var boxes = new List<Rectangle>(pages);
            for (int i = 1; i <= pages; i++)
                boxes.Add(pdf.GetPage(i).GetPageSize());
            return (pages, boxes);
        }

        // ----------------- 1) Crear plantilla -----------------
        [HttpPost("companies/{companyId:int}/templates")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<CompanyDocsTemplateCreateResponseDto>> CreateTemplate(
            int companyId,
            [FromForm] CompanyDocsCreateTemplateForm form)
        {
            if (!await _db.Companies.AsNoTracking().AnyAsync(c => c.Id == companyId))
                return NotFound($"Company {companyId} not found.");

            var pdfFile = form.PdfFile;
            if (pdfFile is null || pdfFile.Length == 0) return BadRequest("PDF file is required.");
            if (!IsPdf(pdfFile)) return BadRequest("Only PDF files are allowed.");
            if (pdfFile.Length > 25 * 1024 * 1024) return BadRequest("PDF exceeds 25 MB.");

            // Lee PDF a memoria
            await using var rawMs = new MemoryStream();
            await pdfFile.CopyToAsync(rawMs);
            rawMs.Position = 0;

            // Info PDF
            var (pageCount, pageBoxes) = ReadPdfInfo(rawMs);
            rawMs.Position = 0;

            // Hash
            string sha256Hex;
            using (var sha = SHA256.Create())
                sha256Hex = Convert.ToHexString(sha.ComputeHash(rawMs.ToArray())).ToLowerInvariant();

            // Valida FieldsJson (si viene)
            var fields = new List<TemplateFieldDto>();
            var fieldsJson = form.FieldsJson?.Trim();

            if (!string.IsNullOrWhiteSpace(fieldsJson))
            {
                // Si vino doblemente serializado desde Swagger: "\"[{...}]\""
                if (fieldsJson!.StartsWith("\"") && fieldsJson.EndsWith("\""))
                {
                    try { fieldsJson = JsonSerializer.Deserialize<string>(fieldsJson) ?? fieldsJson; } catch { }
                }

                try
                {
                    fields = JsonSerializer.Deserialize<List<TemplateFieldDto>>(fieldsJson!, FieldsOptions) ?? new();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "FieldsJson inválido recibido: {FieldsJson}", fieldsJson);
                    return BadRequest("FieldsJson must be a valid JSON array of TemplateFieldDto.");
                }

                foreach (var f in fields)
                {
                    if (f.Page < 1 || f.Page > pageCount)
                        return BadRequest($"Field {f.Label ?? f.Type.ToString()} has invalid Page={f.Page} (1..{pageCount}).");

                    var box = pageBoxes[f.Page - 1];
                    if (f.Width <= 0 || f.Height <= 0)
                        return BadRequest($"Field {f.Label ?? f.Type.ToString()} must have positive Width/Height.");

                    if (f.X < 0 || f.Y < 0 || (f.X + f.Width) > box.GetWidth() || (f.Y + f.Height) > box.GetHeight())
                        return BadRequest($"Field {f.Label ?? f.Type.ToString()} is outside page bounds {box.GetWidth()}x{box.GetHeight()}.");
                }
            }

            // Guarda archivo
            var folderRel = System.IO.Path.Combine("storage", "companies", companyId.ToString(), "documents", "templates");
            var folderAbs = System.IO.Path.Combine(_env.WebRootPath ?? System.IO.Path.Combine(_env.ContentRootPath, "wwwroot"), folderRel);
            Directory.CreateDirectory(folderAbs);

            var fileId = Guid.NewGuid().ToString("N");
            var fileAbs = System.IO.Path.Combine(folderAbs, $"{fileId}.pdf");
            await System.IO.File.WriteAllBytesAsync(fileAbs, rawMs.ToArray());

            // Creador
            var creatorId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var t = new CompanyDocumentTemplate
            {
                CompanyId = companyId,
                Title = form.Title?.Trim() ?? "",
                Description = form.Description?.Trim() ?? "",
                FileUrl = $"{fileId}.pdf",
                Version = string.IsNullOrWhiteSpace(form.Version) ? "v1" : form.Version.Trim(),
                IsActive = true,
                RequireSignature = true,
                IsMandatoryForAllUsers = form.IsMandatoryForAllUsers,
                RequiredRolesCsv = string.IsNullOrWhiteSpace(form.RequiredRolesCsv) ? null : form.RequiredRolesCsv.Trim(),
                FieldsJson = JsonSerializer.Serialize(fields),
                PageCount = pageCount,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = creatorId
            };

            _db.CompanyDocumentTemplates.Add(t);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTemplate), new { templateId = t.Id },
                new CompanyDocsTemplateCreateResponseDto(t.Id, t.FileUrl, t.Version, sha256Hex));
        }

        // ----------------- 2) Obtener plantilla -----------------
        [HttpGet("templates/{templateId:int}")]
        public async Task<ActionResult<CompanyDocsTemplateViewDto>> GetTemplate(int templateId)
        {
            var dto = await _db.CompanyDocumentTemplates.AsNoTracking()
                .Where(x => x.Id == templateId)
                .Select(x => new CompanyDocsTemplateViewDto(
                    x.Id, x.Title, x.Description, x.FileUrl, x.Version,
                    x.PageCount, x.FieldsJson ?? "[]"))
                .FirstOrDefaultAsync();

            return dto is null ? NotFound() : Ok(dto);
        }

        // ----------------- 3) Asignar plantilla a usuarios -----------------
        [HttpPost("companies/{companyId:int}/templates/{templateId:int}/assign")]
        public async Task<IActionResult> AssignTemplate(
            int companyId, int templateId, [FromBody] CompanyDocsAssignTemplateRequestDto body)
        {
            var template = await _db.CompanyDocumentTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == templateId && t.CompanyId == companyId);
            if (template is null) return NotFound("Template not found for this company.");

            IQueryable<User> users = _db.Users.Where(u => u.CompanyId == companyId);

            if (!body.ToAllUsers && !string.IsNullOrWhiteSpace(body.RolesCsv))
            {
                var roles = body.RolesCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                users = users.Where(u => u.UserRole != null && roles.Contains(u.UserRole.ToString()!));
            }

            var userIds = await users.Select(u => u.Id).ToListAsync();
            var already = await _db.CompanyDocumentAssignments
                .Where(a => a.CompanyDocumentTemplateId == templateId && userIds.Contains(a.UserId))
                .Select(a => a.UserId)
                .ToListAsync();

            var toCreate = userIds.Except(already).Select(uid => new CompanyDocumentAssignment
            {
                CompanyDocumentTemplateId = templateId,
                UserId = uid,
                IsRequired = true,
                DueDateUtc = body.DueDateUtc,
                AssignedAtUtc = DateTime.UtcNow,
                Revoked = false
            }).ToList();

            if (toCreate.Count == 0) return Ok(new { Assigned = 0, Message = "No new users to assign." });

            _db.CompanyDocumentAssignments.AddRange(toCreate);
            await _db.SaveChangesAsync();
            return Ok(new { Assigned = toCreate.Count });
        }

        // ----------------- 4) Pendientes del usuario actual -----------------
        [HttpGet("me/pending")]
        public async Task<ActionResult<IEnumerable<CompanyDocsTemplateViewDto>>> GetMyPending()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return Unauthorized();

            var signed = await _db.UserDocumentSignatures.AsNoTracking()
                .Where(s => s.UserId == userId)
                .Select(s => s.CompanyDocumentTemplateId)
                .ToListAsync();

            var assigned = await _db.CompanyDocumentAssignments.AsNoTracking()
                .Where(a => a.UserId == userId && !a.Revoked)
                .Select(a => a.CompanyDocumentTemplateId)
                .Distinct()
                .ToListAsync();

            var result = await _db.CompanyDocumentTemplates.AsNoTracking()
                .Where(t => t.IsActive && t.CompanyId == user.CompanyId && !signed.Contains(t.Id))
                .Where(t => t.IsMandatoryForAllUsers || assigned.Contains(t.Id))
                .Select(t => new CompanyDocsTemplateViewDto(
                    t.Id, t.Title, t.Description, t.FileUrl, t.Version,
                    t.PageCount, t.FieldsJson ?? "[]"))
                .ToListAsync();

            return Ok(result);
        }

        // ----------------- 5) Actualizar solo los campos (calibración) -----------------
        [HttpPut("templates/{templateId:int}/fields")]
        public async Task<IActionResult> UpdateTemplateFields(
            int templateId, [FromBody] List<TemplateFieldDto> fields)
        {
            var t = await _db.CompanyDocumentTemplates.FirstOrDefaultAsync(x => x.Id == templateId);
            if (t == null) return NotFound();

            if (!TryMapPublicToPhysical(t.FileUrl, out var absPath))
                return NotFound("Template file not found.");

            var pdfBytes = await System.IO.File.ReadAllBytesAsync(absPath);
            var (pageCount, pageBoxes) = ReadPdfInfo(pdfBytes);

            // Validaciones
            foreach (var f in fields)
            {
                if (f.Page < 1 || f.Page > pageCount)
                    return BadRequest($"Field {f.Label ?? f.Type.ToString()} has invalid Page={f.Page} (1..{pageCount}).");

                var box = pageBoxes[f.Page - 1];
                if (f.Width <= 0 || f.Height <= 0)
                    return BadRequest($"Field {f.Label ?? f.Type.ToString()} must have positive Width/Height.");
                if (f.X < 0 || f.Y < 0 || (f.X + f.Width) > box.GetWidth() || (f.Y + f.Height) > box.GetHeight())
                    return BadRequest($"Field {f.Label ?? f.Type.ToString()} is outside page bounds {box.GetWidth()}x{box.GetHeight()}.");
            }

            t.FieldsJson = JsonSerializer.Serialize(fields);
            t.PageCount = pageCount;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ----------------- 6) Firmar (legacy) - guarda archivos sin estampar -----------------
        [HttpPost("sign")]
        public async Task<ActionResult<CompanyDocsSignResponseDto>> Sign([FromBody] CompanyDocsSignDocumentRequestDto body)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var template = await _db.CompanyDocumentTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == body.TemplateId);
            if (template is null) return NotFound("Template not found.");

            string? sigPngUrl = null, signedPdfUrl = null;
            var folderRel = System.IO.Path.Combine("storage", "companies", template.CompanyId.ToString(), "documents", "signatures");
            var folderAbs = System.IO.Path.Combine(_env.WebRootPath ?? System.IO.Path.Combine(_env.ContentRootPath, "wwwroot"), folderRel);
            Directory.CreateDirectory(folderAbs);

            if (!string.IsNullOrWhiteSpace(body.DrawnSignatureImageBase64))
            {
                try
                {
                    var b64 = body.DrawnSignatureImageBase64;
                    var comma = b64.IndexOf(',');
                    if (comma >= 0) b64 = b64[(comma + 1)..];
                    var bytes = Convert.FromBase64String(b64);

                    var name = $"{Guid.NewGuid():N}.png";
                    await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(folderAbs, name), bytes);
                    sigPngUrl = PublicUrl(System.IO.Path.Combine(folderRel, name));
                }
                catch (FormatException) { return BadRequest("DrawnSignatureImageBase64 is not valid base64."); }
            }

            if (!string.IsNullOrWhiteSpace(body.SignedPdfBase64))
            {
                try
                {
                    var b64 = body.SignedPdfBase64;
                    var comma = b64.IndexOf(',');
                    if (comma >= 0) b64 = b64[(comma + 1)..];
                    var bytes = Convert.FromBase64String(b64);

                    var name = $"{Guid.NewGuid():N}.pdf";
                    await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(folderAbs, name), bytes);
                    signedPdfUrl = PublicUrl(System.IO.Path.Combine(folderRel, name));
                }
                catch (FormatException) { return BadRequest("SignedPdfBase64 is not valid base64."); }
            }

            var signature = new UserDocumentSignature
            {
                CompanyDocumentTemplateId = template.Id,
                CompanyId = template.CompanyId,
                UserId = userId,
                Method = string.IsNullOrWhiteSpace(body.DrawnSignatureImageBase64) ? ESignMethod.Typed : ESignMethod.Drawn,
                DrawnSignatureImageUrl = sigPngUrl,
                SignedPdfUrl = signedPdfUrl,
                DocumentHashSha256 = body.PdfHashSha256,
                SignedAtUtc = DateTime.UtcNow,
                SignerFullName = body.SignerFullName,
                SignerEmail = body.SignerEmail,
                SignerIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                SignerUserAgent = Request.Headers["User-Agent"].ToString(),
                Version = template.Version
            };

            _db.UserDocumentSignatures.Add(signature);
            await _db.SaveChangesAsync();

            return Ok(new CompanyDocsSignResponseDto(signature.Id, signature.SignedPdfUrl, signature.DrawnSignatureImageUrl));
        }

        // ----------------- 7) Firmar y ESTAMPAR sobre la plantilla -----------------
        [HttpPost("templates/{templateId:int}/sign-and-stamp")]
        public async Task<ActionResult<object>> SignAndStamp(int templateId, [FromBody] SignTemplateRequest req)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var t = await _db.CompanyDocumentTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == templateId);
            if (t == null) return NotFound("Template not found.");

            if (!TryMapPublicToPhysical(t.FileUrl, out var absPath))
                return NotFound("Template file not found.");

            var pdfBytes = await System.IO.File.ReadAllBytesAsync(absPath);
            var fields = JsonSerializer.Deserialize<List<TemplateFieldDto>>(t.FieldsJson ?? "[]") ?? new List<TemplateFieldDto>();
            if (!fields.Any(f => f.Role == req.Role))
                return BadRequest($"No fields configured for role {req.Role}.");

            var stamped = PdfStampHelper.Stamp(pdfBytes, fields, req);

            // Guarda PDF firmado
            var folderRel = System.IO.Path.Combine("storage", "companies", t.CompanyId.ToString(), "documents", "signed");
            var folderAbs = System.IO.Path.Combine(_env.WebRootPath ?? System.IO.Path.Combine(_env.ContentRootPath, "wwwroot"), folderRel);
            Directory.CreateDirectory(folderAbs);

            var outId = $"{templateId}-{req.Role}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var outAbs = System.IO.Path.Combine(folderAbs, $"{outId}.pdf");
            await System.IO.File.WriteAllBytesAsync(outAbs, stamped);

            var outUrl = PublicUrl(System.IO.Path.Combine(folderRel, $"{outId}.pdf"));

            _db.UserDocumentSignatures.Add(new UserDocumentSignature
            {
                CompanyDocumentTemplateId = templateId,
                CompanyId = t.CompanyId,
                UserId = userId,
                Method = string.IsNullOrWhiteSpace(req.SignatureImageBase64) ? ESignMethod.Typed : ESignMethod.Drawn,
                SignedPdfUrl = outUrl,
                SignedAtUtc = DateTime.UtcNow,
                Version = t.Version
            });
            await _db.SaveChangesAsync();

            return Ok(new { url = outUrl });
        }

        [HttpGet("company-template")]
        public async Task<ActionResult<object>> GetCompanyTemplateByLoginUser()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim is null) return Unauthorized("Invalid user.");

            if (!int.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized("Invalid user id.");

            // Usuario + CompanyId
            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.CompanyId })
                .FirstOrDefaultAsync();

            if (user is null) return NotFound("User not found.");
            if (user.CompanyId <= 0) return BadRequest("User has no company assigned.");

            var companyId = user.CompanyId;

            // Template activo más reciente de la compañía
            var template = await _db.CompanyDocumentTemplates
                .AsNoTracking()
                .Where(t => t.CompanyId == companyId && t.IsActive)
                .OrderByDescending(t => t.CreatedAt)   // o .OrderByDescending(t => t.Id)
                .ThenByDescending(t => t.Id)
                .Select(t => new
                {
                    TemplateId = t.Id,
                    t.FileUrl,
                    t.Version,
                    t.PageCount,
                    t.FieldsJson
                })
                .FirstOrDefaultAsync();

            // Si no hay template activo, devolvemos solo el companyId
            if (template is null)
                return Ok(new { companyId, templateId = (int?)null });

            return Ok(new
            {
                companyId,
                templateId = template.TemplateId,
                fileUrl = template.FileUrl,
                version = template.Version,
                pageCount = template.PageCount,
                fieldsJson = template.FieldsJson ?? "[]"
            });
        }
    }

    public static class PdfStampHelper
    {
        public static byte[] Stamp(byte[] pdfBytes, IEnumerable<TemplateFieldDto> fields, SignTemplateRequest req)
        {
            using var src = new MemoryStream(pdfBytes);
            using var dst = new MemoryStream();
            using var pdf = new PdfDocument(new PdfReader(src), new PdfWriter(dst));
            var doc = new Document(pdf);

            var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
            var bold = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);

            byte[]? sigImg = null;
            if (!string.IsNullOrWhiteSpace(req.SignatureImageBase64))
            {
                var b64 = req.SignatureImageBase64;
                var comma = b64.IndexOf(',');
                if (comma >= 0) b64 = b64[(comma + 1)..];
                sigImg = Convert.FromBase64String(b64);
            }

            foreach (var f in fields.Where(x => x.Role == req.Role))
            {
                switch (f.Type)
                {
                    case DocFieldType.Signature:
                        if (sigImg != null)
                        {
                            var img = new Image(ImageDataFactory.Create(sigImg))
                                .ScaleToFit(f.Width, f.Height)
                                .SetFixedPosition(f.Page, f.X, f.Y);
                            doc.Add(img);
                        }
                        else if (!string.IsNullOrWhiteSpace(req.SignatureText))
                        {
                            var p = new Paragraph(req.SignatureText).SetFont(bold)
                                .SetFontSize(MathF.Min(f.Height * 0.7f, 18))
                                .SetTextAlignment(TextAlignment.LEFT).SetMargin(0).SetPadding(0);
                            p.SetFixedPosition(f.Page, f.X + 2, f.Y + (f.Height * 0.15f), f.Width - 4);
                            doc.Add(p);
                        }
                        break;

                    case DocFieldType.Initials:
                        if (!string.IsNullOrWhiteSpace(req.Initials))
                        {
                            var p = new Paragraph(req.Initials).SetFont(bold)
                                .SetFontSize(MathF.Min(f.Height * 0.8f, 14))
                                .SetTextAlignment(TextAlignment.CENTER).SetMargin(0).SetPadding(0);
                            p.SetFixedPosition(f.Page, f.X, f.Y + (f.Height * 0.1f), f.Width);
                            doc.Add(p);
                        }
                        break;

                    case DocFieldType.Date:
                        var d = (req.Date ?? DateTime.UtcNow).ToString("yyyy-MM-dd");
                        var pd = new Paragraph(d).SetFont(font)
                            .SetFontSize(MathF.Min(f.Height * 0.6f, 12))
                            .SetTextAlignment(TextAlignment.LEFT).SetMargin(0).SetPadding(0);
                        pd.SetFixedPosition(f.Page, f.X + 2, f.Y + (f.Height * 0.2f), f.Width - 4);
                        doc.Add(pd);
                        break;

                    case DocFieldType.Text:
                        if (!string.IsNullOrWhiteSpace(req.Name))
                        {
                            var pn = new Paragraph(req.Name).SetFont(font)
                                .SetFontSize(MathF.Min(f.Height * 0.6f, 12))
                                .SetTextAlignment(TextAlignment.LEFT).SetMargin(0).SetPadding(0);
                            pn.SetFixedPosition(f.Page, f.X + 2, f.Y + (f.Height * 0.2f), f.Width - 4);
                            doc.Add(pn);
                        }
                        break;
                }
            }

            doc.Close();
            return dst.ToArray();
        }
    }

    // ----------------- Enums / DTOs mínimos (ajusta o elimina si ya los tienes) -----------------
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DocFieldType { Signature, Initials, Date, Text, Checkbox }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DocSignerRole { Contractor, CompanyRep, Manager, Witness }

    public class TemplateFieldDto
    {
        public DocFieldType Type { get; set; }
        public DocSignerRole Role { get; set; }
        public int Page { get; set; }      // 1-based
        public float X { get; set; }       // puntos (origen abajo-izquierda)
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string? Label { get; set; }
        public bool Required { get; set; } = true;
    }

    public class SignTemplateRequest
    {
        public DocSignerRole Role { get; set; }
        public string? Initials { get; set; }
        public string? Name { get; set; }
        public DateTime? Date { get; set; }
        public string? SignatureImageBase64 { get; set; } // "data:image/png;base64,..." o base64
        public string? SignatureText { get; set; }        // alternativa si no mandas imagen
    }

    // Si YA existen en tu proyecto, puedes borrar estos records y usar los tuyos:
    public record CompanyDocsTemplateCreateResponseDto(int TemplateId, string FileUrl, string Version, string Sha256);
    public record CompanyDocsTemplateViewDto(int Id, string Title, string Description, string FileUrl, string Version, int PageCount, string FieldsJson);

    // Request de creación de plantilla (multipart):
    public class CompanyDocsCreateTemplateForm
    {
        public IFormFile PdfFile { get; set; } = default!;
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public bool IsMandatoryForAllUsers { get; set; }
        public string? RequiredRolesCsv { get; set; }
        public string? FieldsJson { get; set; } // ← JSON de TemplateFieldDto[]
    }

    public class CompanyDocsAssignTemplateRequestDto
    {
        public bool ToAllUsers { get; set; }
        public string? RolesCsv { get; set; }
        public DateTime? DueDateUtc { get; set; }
    }

    public class CompanyDocsSignDocumentRequestDto
    {
        public int TemplateId { get; set; }
        public string? DrawnSignatureImageBase64 { get; set; } // opcional (firma dibujada)
        public string? SignedPdfBase64 { get; set; }           // opcional (PDF ya firmado)
        public string? PdfHashSha256 { get; set; }
        public string? SignerFullName { get; set; }
        public string? SignerEmail { get; set; }
    }

    public record CompanyDocsSignResponseDto(int SignatureId, string? SignedPdfUrl, string? DrawnSignatureImageUrl);
}
