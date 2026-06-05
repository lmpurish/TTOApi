using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using TToApp.DTOs;
using TToApp.Helpers;
using TToApp.Model;
using TToApp.Services;
using TToApp.Services.Auth;
using TToApp.Services.CommunicationRecipient;
using TToApp.Constants;
using TToApp.Services.Notifications;
using static User;




[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly ApplicationDbContext _authContext;
    private readonly EmailService _emailService;
    private readonly IConfiguration _config;
    private readonly WhatsAppService _whatsAppService;
    private readonly IApplicantContactService _applicantContactService;
    private readonly IJwtService _jwtService;
    private readonly ISensitiveDataProtector _protector;
    private readonly ILogger<UserController> _logger;
    private readonly ICommunicationRecipientService _communicationRecipients;
    private readonly INotificationService _notificationService;

    public UserController(ApplicationDbContext authContext, EmailService emailService, IConfiguration config, WhatsAppService whatsAppService,
        IApplicantContactService applicantContactService, IJwtService jwtService, ISensitiveDataProtector protector, ICommunicationRecipientService communicationRecipients,
        ILogger<UserController> logger, INotificationService notificationService)
    {
        _authContext = authContext ?? throw new ArgumentNullException(nameof(authContext));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _config = config;
        _whatsAppService = whatsAppService;
        _applicantContactService = applicantContactService;
        _jwtService = jwtService;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _communicationRecipients = communicationRecipients;
        _notificationService = notificationService;
        _logger = logger;
    }

    [HttpPost("authenticate")]
    public async Task<IActionResult> Authenticate([FromBody] AuthenticationDto userObj)
    {
        if (userObj == null || string.IsNullOrWhiteSpace(userObj.Login) || string.IsNullOrWhiteSpace(userObj.Password))
            return BadRequest(new { Message = "Login and password are required." });

        var hasEmail = userObj.Login.Contains('@');
        User? user = null;

        if (hasEmail)
        {
            user = await _authContext.Users
                .Where(u => u.Email == userObj.Login.Trim())
                .Include(u => u.Company)
                .Include(u => u.Warehouse)
                .FirstOrDefaultAsync();
        }
        else
        {
            var phone = userObj.Login.Trim()
                .Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Replace(".", "");

            var userIds = await _authContext.Set<UserProfile>()
                .Where(p => p.PhoneNumber == phone)
                .Select(p => p.Id)
                .ToListAsync();

            if (userIds.Any())
            {
                var candidates = await _authContext.Users
                    .Where(u => userIds.Contains(u.Id) && !string.IsNullOrEmpty(u.Password))
                    .Include(u => u.Company)
                    .Include(u => u.Warehouse)
                    .ToListAsync();

                user = candidates.FirstOrDefault(u =>
                    PasswordHasher.VerifiPassword(userObj.Password, u.Password!));
            }
        }

        if (user == null)
            return NotFound(new { Message = "User not Found" });

        if (hasEmail && (string.IsNullOrEmpty(user.Password) || !PasswordHasher.VerifiPassword(userObj.Password, user.Password)))
            return BadRequest(new { Message = "Password is Incorrect!" });

        if (!user.IsActive)
            return BadRequest(new { Message = "That account is not Active!" });

        Company? company = user.Company;
        if (company == null && user.UserRole == global::User.Role.CompanyOwner)
            company = await _authContext.Companies.FirstOrDefaultAsync(c => c.OwnerId == user.Id);

        user.Token = _jwtService.CreateJwtToken(user, company);

        if (string.IsNullOrEmpty(user.Email))
        {
            await _notificationService.NotifyAsync(
                userId: user.Id,
                title: "Email required",
                message: "Please update your email address to keep your account secure.",
                type: NotificationType.Warning,
                url: "/profile/email",
                source: "Account"
            );
        }

        return Ok(new
        {
            Token = user.Token,
            IsFirstLogin = user.IsFirstLogin,
            RequiresEmailUpdate = string.IsNullOrEmpty(user.Email),
            Message = "Login success!"
        });
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterUser([FromBody] RegisterUserDto body)
    {
        if (body is null)
            return BadRequest(new { Message = "Payload requerido." });

        // 1) Ruta de referencia: si viene ReferralCode, solo redirigimos
        if (!string.IsNullOrWhiteSpace(body.ReferralCode))
        {
            var company = await _authContext.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ReferralCode == body.ReferralCode);

            if (company is null)
                return NotFound(new { Message = "Invalid reference code." });

            var url = company.WebsiteUrl;
            if (string.IsNullOrWhiteSpace(url))
                return BadRequest(new { Message = "The company does not have a WebsiteUrl configured." });

            // Normaliza por si viene sin esquema
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = $"https://{url}";

            // Redirección (302). Si prefieres forzar GET tras POST, usa 303:
            // return StatusCode(StatusCodes.Status303SeeOther, null).WithHeader("Location", url);  // alternativa manual
            return  Ok(new { redirectUrl = url,
                             companyName = company.Name   }); // 302 Found
        }

        // 2) Registro normal de CompanyOwner (si NO hay ReferralCode)
        var email = body.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(body.Password))
            return BadRequest(new { Message = "Email and password are required when there is no referral code." });

        if (await CheckEmailExistAsync(email))
            return BadRequest(new { Message = "Email Already Exists!" });

        var user = new User
        {
            Email = email,
            Password = PasswordHasher.HashPassword(body.Password),
            Token = string.Empty,
            UserRole = global::User.Role.CompanyOwner,
            IsActive = true,
            Name = body.Name,
            LastName = body.Lastname
            // Mapea aquí otros campos desde body si los agregas
        };

        await _authContext.Users.AddAsync(user);
        await _authContext.SaveChangesAsync();

        return Ok(new { Message = "Company owner registered successfully!" });
    }
    [HttpPost("registerUserByMovilApp")]
    public async Task<IActionResult> RegisterUserByMovilApp([FromBody] UserDTO userObj)
    {
        if (userObj is null) return BadRequest(new { message = "Invalid payload." });
        if (string.IsNullOrWhiteSpace(userObj.Email) || string.IsNullOrWhiteSpace(userObj.Password))
            return BadRequest(new { message = "Email and Password are required." });

        if (await _authContext.Users.AnyAsync(u => u.Email == userObj.Email))
            return BadRequest(new { message = "Email Already Exists!" });

        // Sugerido: transacción por consistencia
        using var tx = await _authContext.Database.BeginTransactionAsync();

        var user = new User
        {
            Name = userObj.Name?.Trim() ?? "",
            LastName = userObj.LastName?.Trim() ?? "",
            Email = userObj.Email.Trim(),
            Password = PasswordHasher.HashPassword(userObj.Password),
            UserRole = global::User.Role.Driver,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _authContext.Users.AddAsync(user);
        await _authContext.SaveChangesAsync(); // ✅ ahora user.Id ya existe

        // Variante A: UserProfile con FK UserId (recomendada)
        var userProfile = new UserProfile
        {
            Id = user.Id,              // ✅ FK
            PhoneNumber = userObj.PhoneNumber
        };
        await _authContext.UserProfiles.AddAsync(userProfile);

        await _authContext.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new { message = "Driver successfully registered!" });
    }

    [AllowAnonymous]
    [HttpPost("apply")]
    public async Task<IActionResult> ApplicationJob([FromBody] DriverApplicationRequest applicationRequest, CancellationToken ct)
    {
        if (applicationRequest?.User is null || applicationRequest.Vehicle is null)
            return BadRequest(new { Message = "Invalid request: User or Vehicle object is null" });

        var u = applicationRequest.User;
        var v = applicationRequest.Vehicle;

        // Email
        var emailNorm = u.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(emailNorm))
            return BadRequest(new { Message = "User email is required." });

        var exists = await _authContext.Users.AsNoTracking()
            .AnyAsync(x => x.Email != null && x.Email.ToLower() == emailNorm, ct);
        if (exists)
            return BadRequest(new { Message = "Sorry, that email is already in use!" });

        // 🚩 Warehouse obligatorio y de ahí sacamos la compañía
        if (!u.metroId.HasValue)
            return BadRequest(new { Message = "MetroId is required." });

        var whInfo = await _authContext.Metro.AsNoTracking()
            .Where(w => w.Id == u.metroId.Value)
            .Select(w => new { w.Id, w.City, w.CompanyId })
            .FirstOrDefaultAsync(ct);

        if (whInfo is null)
            return BadRequest(new { Message = "Metro not found." });

        if (whInfo.CompanyId==null)
            return BadRequest(new { Message = "Metro has no company assigned." });

        // Vehículo
        if (string.IsNullOrWhiteSpace(v.Make) || string.IsNullOrWhiteSpace(v.Model))
            return BadRequest(new { Message = "Vehicle make and model are required." });

        // Teléfono -> UserProfile (PK compartida)
        string? phone = u.PhoneNumber?
            .Trim()
            .Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Replace(".", "");
        if (phone?.Length > 20)
            return BadRequest(new { Message = "Phone number too long." });

        await using var tx = await _authContext.Database.BeginTransactionAsync(ct);
        try
        {
            // 👇 CompanyId viene del warehouse seleccionado
            var user = new User
            {
                Name = u.Name?.Trim(),
                LastName = u.LastName?.Trim(),
                Email = emailNorm,
                Password = PasswordHasher.HashPassword(u.Password),
                IsActive = false,
                AcceptsSMSNotifications = u.AcceptsSMSNotifications,
                CompanyId = whInfo.CompanyId,           // ✅ derivado
                MetroId = u.metroId,                // ✅ consistente
                UserRole = global::User.Role.Applicant
            };
            await _authContext.Users.AddAsync(user, ct);
            await _authContext.SaveChangesAsync(ct);

            var profile = new UserProfile
            {
                Id = user.Id,                           // ✅ PK compartida
                PhoneNumber = phone
            };
            await _authContext.UserProfiles.AddAsync(profile, ct);
            await _authContext.SaveChangesAsync(ct);

            var vehicle = new Vehicle
            {
                UserId = user.Id,
                Make = v.Make?.Trim(),
                Model = v.Model?.Trim(),
                IsDefault = true,
            };
            await _authContext.Vehicles.AddAsync(vehicle, ct);
            await _authContext.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            // Emails / notificaciones
            var placeholders = new Dictionary<string, string>
            {
                { "Name", user.Name ?? "" },
                { "LastName", user.LastName ?? "" },
                { "Email", user.Email ?? "" },
                { "PhoneNumber", profile.PhoneNumber ?? "" },
                { "Make", vehicle.Make ?? "" },
                { "Model", vehicle.Model ?? "" },
                { "Locality", whInfo.City ?? "" }
            };

        //      var managerEmail = GetEmailManager(user);
        //    if (!string.IsNullOrWhiteSpace(managerEmail))
        //      {
        //         await _emailService.SendEmailAsync(
        //            toEmail: managerEmail,
        //              subject: "New driver on the way!",
        //             "ApplicationTemplate.cshtml",
        //            placeholders: placeholders,
        //            copy: false
        //         );
        //      }

        //  var adminEmails = await _authContext.Users.AsNoTracking()
        //         .Where(u2 => u2.UserRole == global::User.Role.Admin && !string.IsNullOrEmpty(u2.Email))
        //        .Select(u2 => u2.Email!)
        //         .ToListAsync(ct);
   
            var warehouseIds = await _authContext.Warehouses
                    .AsNoTracking()
                    .Where(w => w.MetroId == user.MetroId)
                    .Select(w => w.Id)
                    .ToListAsync(ct);


       /*      var authorizedEmployeeEmails = await _authContext.Permits
        .AsNoTracking()
        .Where(p => p.WarehouseId == user.WarehouseId
               && p.UserPermit == Permit.Notification) // ó p.UserPermit == 0 si lo manejas como int
        .Select(p => p.User!.Email)
        .Where(email => !string.IsNullOrEmpty(email))
        .Distinct()
        .ToListAsync(ct);
          var recipients = await _communicationRecipients.GetRecipientsForEventAsync(
            companyId: whInfo.CompanyId,
            warehouseId: user.WarehouseId,
            eventType: CommunicationEventTypes.NewDriverApplication,
            channel: CommunicationChannels.Email
        );*/

            var recipients = await _communicationRecipients.GetRecipientsForEventAsync(
                companyId: whInfo.CompanyId,
                warehouseIds: warehouseIds.Any() ? warehouseIds : null,
                eventType: CommunicationEventTypes.NewDriverApplication,
                channel: CommunicationChannels.Email
            );


            foreach (var email in recipients.Select(r => r.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct())
            {
                await _emailService.SendEmailAsync(
                    toEmail: email!,
                    subject: "New driver on the way!",
                    "ApplicationTemplate.cshtml",
                    placeholders: placeholders,
                    copy: false
                );
            }
            //  foreach (var email in adminEmails)
            // {
            //     await _emailService.SendEmailAsync(
            //         toEmail: email,
            //          subject: "New driver on the way!",
            //        "ApplicationTemplate.cshtml",
            //         placeholders: placeholders,
            //          copy: false
            //      );
            //  }

            var okUserMail = await _emailService.SendEmailAsync(
                toEmail: user.Email!,
                subject: "Thank you",
               "ApplicationConfirmation.cshtml",
                placeholders: placeholders,
                copy: false
            );
            if (!okUserMail)
                return BadRequest(new { Message = "Failed to send confirmation email." });

            // Contacto si el warehouse contrata
            var whIsHiring = await _authContext.Warehouses.AsNoTracking()
                .Where(w => w.Id == user.WarehouseId)
                .Select(w => w.IsHiring)
                .FirstOrDefaultAsync(ct);

            if (whIsHiring)
                await _applicantContactService.ContactApplicantAsync(user.Id);

            return Ok(new { Message = "Thank you for applying with us!" });
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            var inner = ex.InnerException?.Message ?? ex.GetBaseException().Message ?? ex.Message;
            return StatusCode(500, new { Message = "DB update error.", Error = inner });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return StatusCode(500, new { Message = "An error occurred while processing your request.", Error = ex.Message });
        }
    }
    [AuthorizePrivateFile]
    [HttpGet("avatar/{filename}")]
    public IActionResult GetAvatar(string filename)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "PrivateFiles/uploads/", "Avatares", filename);

        if (!System.IO.File.Exists(path))
            return NotFound();

        var mimeType = GetMimeType(path);
        var fileBytes = System.IO.File.ReadAllBytes(path);
        return File(fileBytes, mimeType);
    }

    [HttpGet("ssnn/{filename}")]
    [AuthorizePrivateFile] // ✅ Llama automáticamente la validación
    public IActionResult GetSSN(string filename)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "PrivateFiles/uploads/socialSecurities", filename);

        if (!System.IO.File.Exists(path))
            return NotFound();

        var mimeType = GetMimeType(path);
        var fileBytes = System.IO.File.ReadAllBytes(path);
        return File(fileBytes, mimeType, filename);
    }
    [HttpGet("driverLicenseUrl/{filename}")]
    [AuthorizePrivateFile] // ✅ Llama automáticamente la validación
    public IActionResult GetDriverLicense(string filename)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "PrivateFiles/uploads/licenses", filename);

        if (!System.IO.File.Exists(path))
            return NotFound();

        var mimeType = GetMimeType(path);
        var fileBytes = System.IO.File.ReadAllBytes(path);
        return File(fileBytes, mimeType, filename);
    }
    [HttpGet("insuranceUrl/{filename}")]
    [AuthorizePrivateFile] // ✅ Llama automáticamente la validación
    public IActionResult GetInsurance(string filename)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "PrivateFiles/uploads/insurance", filename);

        if (!System.IO.File.Exists(path))
            return NotFound();

        var mimeType = GetMimeType(path);
        var fileBytes = System.IO.File.ReadAllBytes(path);
        return File(fileBytes, mimeType, filename);
    }


    private string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    [Authorize]
    [HttpPost("upload/avatar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAvatar([FromForm] AvatarUploadDto dto)
    {
        var avatar = dto.Avatar;
        if (avatar == null || avatar.Length == 0)
            return BadRequest("No file uploaded.");

        var allowedTypes = new[] { ".jpg", ".jpeg", ".png", ".gif" };
        var ext = Path.GetExtension(avatar.FileName).ToLowerInvariant();

        if (!allowedTypes.Contains(ext))
            return BadRequest("Unsupported file type.");

        // Obtener ID del usuario autenticado
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null)
            return Unauthorized("Invalid user.");

        var userId = int.Parse(userIdClaim.Value);
        var user = await _authContext.Users.FindAsync(userId);
        if (user == null)
            return NotFound("User not found.");

        // Crear nombre único y guardar archivo
        var fileName = $"{user.Id}_{Guid.NewGuid().ToString().Substring(0, 8)}{ext}";
        var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "PrivateFiles/uploads", "Avatares");
        Directory.CreateDirectory(folderPath); // crea si no existe
        var filePath = Path.Combine(folderPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await avatar.CopyToAsync(stream);
        }

        // Actualizar nombre del avatar en la base de datos
        user.AvatarUrl = fileName;
        await _authContext.SaveChangesAsync();

        return Ok(new { avatar = fileName });
    }



    // GET: api/User/5
    [Authorize]
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _authContext.Users
            .Include(u => u.Profile)
            .Include(u => u.Vehicles)
            .Include(w => w.Warehouse)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound();
        }

        var userDto = new UserInfoDto
        {
            Id = user.Id,
            Name = user.Name,
            LastName = user.LastName,
            Email  = user.Email,
            Phone = user.Profile.PhoneNumber,
            
            Avatar = user.AvatarUrl,
            Warehouse = user.Warehouse == null ? null : new WarehouseDTO
            {
                Id = user.Warehouse.Id,
                City = user.Warehouse.City,
                Company = user.Warehouse.Company
            },
            Vehicles = user.Vehicles.Select(v => new VehicleDto
            {
                Id = v.Id,
                Make = v.Make,
                Model = v.Model
            }).ToList()

        };

        return Ok(userDto);
    }

    [Authorize]
    [HttpGet("driversByRol")]
    public async Task<IActionResult> GetEmployees()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
        if (userIdClaim == null)
            return Unauthorized(new { message = "Invalid token" });

        if (!int.TryParse(userIdClaim.Value, out int userId))
            return Unauthorized(new { message = "Invalid user id" });

        var user = await _authContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return NotFound(new { message = "User not found." });

        IQueryable<User> query = _authContext.Users.AsNoTracking();

        // Admin / CompanyOwner / Assistant => ven todos los usuarios de la compañía excepto Applicant
        if (user.UserRole.HasValue &&
            (user.UserRole.Value == global::User.Role.Admin ||
             user.UserRole.Value == global::User.Role.CompanyOwner ||
             user.UserRole.Value == global::User.Role.Assistant))
        {
            query = query.Where(u =>
                u.CompanyId == user.CompanyId &&
                u.UserRole != global::User.Role.Applicant);
        }
        // Manager => solo drivers de su warehouse
        else if (user.UserRole.HasValue && user.UserRole.Value == global::User.Role.Manager)
        {
            if (!user.WarehouseId.HasValue)
                return BadRequest(new { message = "Manager does not have an assigned warehouse." });

            query = query.Where(u =>
                u.WarehouseId == user.WarehouseId &&
                u.UserRole == global::User.Role.Driver);
        }
        else
        {
            return Forbid();
        }

        var result = await query
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.LastName,
                u.Email,
                u.IsActive,
                u.UserRole,
                u.IdentificationNumber,
                u.WarehouseId,
                u.AvatarUrl,

                Warehouse = u.Warehouse != null ? new
                {
                    u.Warehouse.City,
                    u.Warehouse.Company
                } : null,

                Profile = u.Profile != null ? new
                {
                    PhoneNumber = u.Profile.PhoneNumber,
                    ssn = u.Profile.SsnLast4,
                    ssnUrl = u.Profile.SocialSecurityUrl,
                    address = u.Profile.Address,
                    city = u.Profile.City,
                    zipcode = u.Profile.ZipCode,
                    state = u.Profile.State
                } : null,

                Account = u.Accounts
                    .Where(a => a.IsDefault)
                    .Select(a => new
                    {
                        a.Id,
                        accountNumber = a.AccountNumber,
                        routingNumber = a.RoutingNumber
                    })
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(result);
    }

    [Authorize]
    [HttpGet("active-by-warehouse")]
    public async Task<IActionResult> GetActiveUsersByWarehouse([FromQuery] int? warehouseId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
        if (userIdClaim == null)
            return Unauthorized(new { message = "Invalid token" });

        if (!int.TryParse(userIdClaim.Value, out int userId))
            return Unauthorized(new { message = "Invalid user id" });

        var currentUser = await _authContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (currentUser == null)
            return NotFound(new { message = "User not found." });

        int targetWarehouseId;

        // Manager => usa su propio warehouse
        if (currentUser.UserRole.HasValue && currentUser.UserRole.Value == global::User.Role.Manager)
        {
            if (!currentUser.WarehouseId.HasValue)
                return BadRequest(new { message = "Manager does not have an assigned warehouse." });

            targetWarehouseId = currentUser.WarehouseId.Value;
        }
        // Admin / CompanyOwner / Assistant => warehouseId es obligatorio
        else if (currentUser.UserRole.HasValue &&
            (currentUser.UserRole.Value == global::User.Role.Admin ||
             currentUser.UserRole.Value == global::User.Role.CompanyOwner ||
             currentUser.UserRole.Value == global::User.Role.Assistant))
        {
            if (!warehouseId.HasValue)
                return BadRequest(new { message = "warehouseId is required for Admin, CompanyOwner, or Assistant." });

            targetWarehouseId = warehouseId.Value;
        }
        else
        {
            return Forbid();
        }

        var users = await _authContext.Users
            .AsNoTracking()
            .Where(u =>
                u.WarehouseId == targetWarehouseId &&
                u.IsActive &&
                u.UserRole != global::User.Role.Applicant)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.LastName,
                u.Email,
                u.IsActive,
                u.UserRole,
                u.IdentificationNumber,
                u.WarehouseId,
                u.AvatarUrl,

                Warehouse = u.Warehouse != null ? new
                {
                    u.Warehouse.Id,
                    u.Warehouse.City,
                    u.Warehouse.Company
                } : null,

                Profile = u.Profile != null ? new
                {
                    PhoneNumber = u.Profile.PhoneNumber,
                    ssn = u.Profile.SsnLast4,
                    ssnUrl = u.Profile.SocialSecurityUrl,
                    address = u.Profile.Address,
                    city = u.Profile.City,
                    zipcode = u.Profile.ZipCode,
                    state = u.Profile.State
                } : null,

                Account = u.Accounts
                    .Where(a => a.IsDefault)
                    .Select(a => new
                    {
                        a.Id,
                        accountNumber = a.AccountNumber,
                        routingNumber = a.RoutingNumber
                    })
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(users);
    }
    [Authorize]
    [HttpGet("applicantByRol")]
    public async Task<ActionResult<List<User>>> GetApplicant()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
        if (userIdClaim == null)
            return Unauthorized(new { Message = "Invalid token" });

        int userId = int.Parse(userIdClaim.Value);
        // Obtener el usuario que solicita la información
        var user = await _authContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        // Si el usuario es Admin o Comapny Owner, devolver todos los usuarios
        if (user.UserRole.HasValue && (user.UserRole.Value == global::User.Role.Admin || user.UserRole.Value == global::User.Role.CompanyOwner || user.UserRole.Value == global::User.Role.Assistant || user.UserRole.Value == global::User.Role.Recruiter))
        {
            var allUsers = await _authContext.Users
    .AsNoTracking()
    .Where(u => u.CompanyId == user.CompanyId &&
                u.UserRole == global::User.Role.Applicant)
    .Select(u => new
    {
        // Campos del usuario
        u.Name,
        u.LastName,
        u.Email,
        u.IsActive,
        u.UserRole,
        u.WarehouseId,
        u.UpdatedAt,
        u.WasContacted,
        u.IsFirstLogin,
        u.Id,
        u.Stage,
        u.AvatarUrl,
        

        // Recruiter info
        Recruiter = u.RecruiterId != null
            ? new
            {
                Id = u.RecruiterId,
                FirstName = u.Recruiter!.Name,
                LastName = u.Recruiter!.LastName
            }
            : null,

        // Warehouse info
        Warehouse = u.Warehouse != null
            ? new
            {
                u.Warehouse.City,
                u.Warehouse.Company
            }
            : null,
        Metro = u.Metro!=null ? new
        {
            u.Metro.City,
        }:null,

        // Profile info
        Profile = u.Profile != null
            ? new
            {
                PhoneNumber = u.Profile.PhoneNumber,
                ssn = u.Profile.SsnLast4,
                ssnUrl = u.Profile.SocialSecurityUrl,
                address = u.Profile.Address,
                city = u.Profile.City,
                zipcode = u.Profile.ZipCode,
                state = u.Profile.State,
                dob = u.Profile.DateOfBirth
            }
            : null,

        // Vehicle (primer vehículo asociado)
        Vehicle = u.Vehicles
            .Select(v => new
            {
                v.Make,
                v.Model,
                v.Type,
            })
            .FirstOrDefault(),

        // Account (cuenta default para payroll, si existe)
        Account = u.Accounts
            .Where(a => a.IsDefault)
            .Select(a => new
            {
                a.Id,
                accountNumber = a.AccountNumber,
                routingNumber = a.RoutingNumber
            })
            .FirstOrDefault(),

        // 🔥 Activities (historial de reclutamiento del applicant)
        Activities = _authContext.ApplicantActivity
            .Where(act => act.ApplicantId == u.Id)
            .OrderByDescending(act => act.CreateAt)
            .Select(act => new
            {
                act.Id,
                act.ApplicantId,
                act.RecruiterId,
                activity = act.Activity,   // <-- ojo al nombre en tu DB
                act.Message,
                act.CreateAt,

                // Nombre del recruiter que hizo la acción
                RecruiterName = act.Recruiter != null
                    ? act.Recruiter.Name + " " + act.Recruiter.LastName
                    : null
            })
            .ToList() // <- EF Core sabe materializar subcolecciones con .ToList() en proyección
    })
    .ToListAsync();

            return Ok(allUsers);

        }

        // Si el usuario es Manager, devolver solo los Applicants del mismo Warehouse
        if (user.UserRole.HasValue && user.UserRole.Value == global::User.Role.Manager)
        {
            if (!user.WarehouseId.HasValue)
            {
                return BadRequest(new { message = "El Manager no tiene un almacén asignado." });
            }

            var warehouseId = user.WarehouseId;

            // Si MetroId está en Warehouse, resuélvelo aparte:
            int? metroId = await _authContext.Warehouses
                .AsNoTracking()
                .Where(w => w.Id == warehouseId)
                .Select(w => (int?)w.MetroId)
                .FirstOrDefaultAsync();

            var drivers = await _authContext.Users
                .AsNoTracking()
                .Include(u => u.Vehicles)
                // (opcional) si quieres usar u.Warehouse en el projection, puedes incluirlo también:
                .Include(u => u.Warehouse)
                .Where(u =>
                    u.UserRole == global::User.Role.Applicant &&
                    (
                        (
                            u.MetroId == metroId &&
                            u.WarehouseId == null
                        )
                        ||
                        (
                            u.WarehouseId == warehouseId
                        )
                    )
                )
                .Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.LastName,
                    u.Email,
                    u.IsActive,
                    u.UserRole,
                    u.WarehouseId,
                    u.AvatarUrl,
                    u.WasContacted,
                    u.IsFirstLogin,
                    u.UpdatedAt,
                    Metro = u.Metro != null ? new
                    {
                        u.Metro.Id,
                        u.Metro.City
                    } : null,

                    Warehouse = u.Warehouse != null ? new
                    {
                        u.Warehouse.City,
                        u.Warehouse.Company
                    } : null,

                    Profile = u.Profile != null ? new
                    {
                        PhoneNumber = u.Profile.PhoneNumber,
                        ssn = u.Profile.SsnLast4,
                        address = u.Profile.Address,
                        city = u.Profile.City,
                        zipcode = u.Profile.ZipCode,
                        state = u.Profile.State,
                        dob = u.Profile.DateOfBirth
                    } : null,

                    Vehicle = u.Vehicles
                        .Select(v => new { v.Make, v.Model,v.Type })
                        .FirstOrDefault(),

                    Account = u.Accounts
                        .Where(a => a.IsDefault)
                        .Select(a => new
                        {
                            a.Id,
                            accountNumber = a.AccountNumber,
                            routingNumber = a.RoutingNumber
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(drivers);
        }

        // Si el usuario es Assistant o cualquier otro, no tiene permisos
        return Forbid();
    }

    [HttpGet("getManager/{userId}")]
    public async Task<ActionResult<int?>> GetManager(int userId)
    {
        // 🔍 Obtener el WarehouseId del usuario dado
        var warehouseId = await _authContext.Users
            .Where(u => u.Id == userId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();

        if (warehouseId == null)
        {
            return NotFound("El usuario no tiene un WarehouseId asociado.");
        }

        // 🔍 Buscar el ID del manager del warehouse
        var managerId = await _authContext.Users
            .Where(u => u.UserRole.Value == global::User.Role.Manager && u.WarehouseId == warehouseId)
            .Select(u => (int?)u.Id) // 🔥 Convertimos a int? para evitar errores
            .FirstOrDefaultAsync();

        if (managerId == null)
        {
            return NotFound("No se encontró un manager para este warehouse.");
        }

        return Ok(managerId); // ✅ Retorna solo el ID del manager
    }





    [HttpPost("complete-profile")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> CompleteProfile([FromForm] UserProfileRequest request)
    {
        if (request == null) return BadRequest(new { Message = "Invalid profile data." });

        // ✅ Usa el usuario del token (quita el hardcode 593)
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

        await using var tx = await _authContext.Database.BeginTransactionAsync();

        var user = await _authContext.Users
            .Include(u => u.Profile)
            .Include(u => u.Warehouse)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound(new { Message = "User not found." });

        // ✅ Asegura el profile SIN guardar aún
        if (user.Profile == null)
        {
            user.Profile = new UserProfile { Id = user.Id };
            _authContext.UserProfiles.Add(user.Profile);
        }

        try
        {
            if (request.Address != null) { 
                user.Profile.Address = request.Address;
            }
            if (request.City != null) { 
                user.Profile.City = request.City;
            }
            if (request.State != null) { 
                user.Profile.State = request.State; 
            }
            if( request.ZipCode != null)
            {
                user.Profile.ZipCode = request.ZipCode;
            }

            // ---------- Validaciones de negocio ----------
            if (request.DateOfBirth.HasValue)
            {
                var dob = request.DateOfBirth.Value.Date;
                if (dob > DateTime.UtcNow.Date.AddYears(-18))
                    return BadRequest(new { Message = "Driver must be at least 18 years old." });
            }

            if (request.ExpDriverLicense.HasValue)
            {
                var exp = request.ExpDriverLicense.Value.Date;
                if (exp < DateTime.UtcNow.Date)
                    return BadRequest(new { Message = "Driver license is expired." });
            }

            if (request.ExpInsurance.HasValue) {
                var expInsurance = request.ExpInsurance.Value.Date;
                if (expInsurance < DateTime.UtcNow.Date)
                    return BadRequest(new { Message = "Insurance is expired" });
            }

            // ---------- Asignaciones seguras ----------
            user.Profile.PhoneNumber = request.PhoneNumber;
            user.Profile.DriverLicenseNumber = request.DriverLicenseNumber;
            if (request.ExpInsurance.HasValue)
                user.Profile.ExpInsurance = DateOnly.FromDateTime(request.ExpInsurance.Value);
            // ⚠️ Mantén coherencia de tipos con tu modelo:
            // Si DateOfBirth en tu modelo es DateTime?:
            if (request.DateOfBirth.HasValue)
                user.Profile.DateOfBirth = DateOnly.FromDateTime(request.DateOfBirth.Value);
            else
                user.Profile.DateOfBirth = null;

            // Si ExpDriverLicense en tu modelo es DateOnly?:
            if (request.ExpDriverLicense.HasValue)
                user.Profile.ExpDriverLicense = DateOnly.FromDateTime(request.ExpDriverLicense.Value);
            else
                user.Profile.ExpDriverLicense = null;



            // SSN seguro
            if (!string.IsNullOrWhiteSpace(request.SocialSecurityNumber))
            {
                var ssnRaw = request.SocialSecurityNumber ?? string.Empty;
                var ssn = ssnRaw.Replace("-", "").Trim();

                if (ssn.Length != 9)
                {
                    return BadRequest(new { Message = "Invalid SSN." });
                }

                if (_protector == null) return StatusCode(500, new { Message = "Encryption service not available." });

                // Proteger y validar que Protect no devuelva vacío
                var encrypted = _protector.Protect(ssn);

                if (string.IsNullOrWhiteSpace(encrypted))
                {
                    _logger.LogError("Protect returned empty while saving SSN for user {UserId}. ssnRawPreview={Preview}", userId, ssnRaw.Length <= 8 ? ssnRaw : ssnRaw.Substring(ssnRaw.Length - 4));
                    return StatusCode(500, new { Message = "Encryption failed." });
                }

#if DEBUG
                // Optional: roundtrip sanity check en debug/dev solamente
                try
                {
                    var roundtrip = _protector.Unprotect(encrypted) ?? string.Empty;
                    if (roundtrip != ssn)
                    {
                        _logger.LogError("Protect/Unprotect roundtrip mismatch for user {UserId}", userId);
                        return StatusCode(500, new { Message = "Encryption roundtrip mismatch." });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Roundtrip Unprotect failed for user {UserId}", userId);
                    return StatusCode(500, new { Message = "Encryption roundtrip failed." });
                }
#endif

                // Guard: solo sobreescribe SsnEncrypted si resultó un ciphertext válido.
                // Guardamos preview del ciphertext (solo inicio/fin + longitud) para auditoría
                var preview = encrypted.Length <= 16 ? encrypted : $"{encrypted.Substring(0, 8)}...{encrypted.Substring(encrypted.Length - 8)}";
                _logger.LogInformation("SSN Protected for user {UserId} — encryptedLen={Len}, preview={Preview}", userId, encrypted.Length, preview);

                user.Profile.SsnEncrypted = encrypted;
                user.Profile.SsnLast4 = ssn[^4..];
                user.Profile.SsnUpdatedAt = DateTime.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(request?.AccountNumber) && !string.IsNullOrWhiteSpace(request.AccountHolderName) &&
                 !string.IsNullOrWhiteSpace(request.RoutingNumber))
            {
                var existingAccounts = _authContext.Accounts
                    .Where(a => a.UserId == user.Id && a.IsDefault);

                foreach (var acc in existingAccounts)
                    acc.IsDefault = false;

                var Account = new Accounts
                {
                    AccountNumber = request.AccountNumber,
                    RoutingNumber = request.RoutingNumber,
                    FullName = request.AccountHolderName,
                    UserId = user.Id,
                    IsDefault = true,
                };

                _authContext.Accounts.Add(Account);
              
            }


            // ---------- Archivos privados ----------
            if (request.DriverLicense != null)
            {
                if (!IsAllowed(request.DriverLicense, allowPdf: true))
                    return BadRequest(new { Message = "Invalid license file." });
                user.Profile.DrivingLicenseUrl = await SavePrivateAsync(request.DriverLicense, "licenses");
            }

            if (request.SocialSecurityUrl != null)
            {
                if (!IsAllowed(request.SocialSecurityUrl, allowPdf: true))
                    return BadRequest(new { Message = "Invalid SSN file." });
                user.Profile.SocialSecurityUrl = await SavePrivateAsync(request.SocialSecurityUrl, "socialSecurities");
            }

            if (request.AvatarUrl != null)
            {
                if (!IsAllowed(request.AvatarUrl, allowPdf: false))
                    return BadRequest(new { Message = "Invalid avatar file." });
                user.AvatarUrl = await SavePrivateAsync(request.AvatarUrl, "avatars");
            }
            if (request.InsuranceUrl != null)
            {
                if (!IsAllowed(request.InsuranceUrl, allowPdf: true))
                    return BadRequest(new { Message = "Invalid Insurance Card file." });
                user.Profile.InsuranceUrl = await SavePrivateAsync(request.InsuranceUrl, "insurance");
            }

            user.IsFirstLogin = false;
            user.UpdatedAt = DateTime.UtcNow;

            // ✅ Guarda TODO una vez y confirma transacción
            await _authContext.SaveChangesAsync();
            await tx.CommitAsync();

            // ---------- Notificaciones ----------

            _ = Task.Run(async () =>
            {
                try
                {
                    var adminEmails = await _authContext.Users
                .Where(u => u.CompanyId == user.CompanyId
                         && u.UserRole == global::User.Role.Admin
                         && u.Email != null)
                .Select(u => u.Email!)
                .ToListAsync();

            var placeholders = new Dictionary<string, string>
        {
            { "Name", user.Name ?? "" },
            { "LastName", user.LastName ?? "" },
            { "Email", user.Email ?? "" },
            { "PhoneNumber", user.Profile.PhoneNumber ?? "" },
            { "Locality", GetWarehouseCity1(user) ?? "" }
        };

            foreach (var email in adminEmails)
            {
                await _emailService.SendEmailAsync(
                    toEmail: email,
                    subject: "Successfully completed profile!",
                    "CompleteProfileComfirmation.cshtml",
                    placeholders: placeholders,
                    copy: false
                );
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                await _emailService.SendEmailAsync(
                    toEmail: user.Email,
                    subject: "Thank you",
                     "ApplicationConfirmation.cshtml",
                    placeholders: placeholders,
                    copy: false
                );
            }
                }
                catch (Exception ex) { _logger.LogError(ex, "Email notification failed (ignored)."); }
            });
            _logger.LogInformation("CP finished OK for {UserId}", userId);
            return Ok(new { Message = "Profile updated successfully!" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CompleteProfile failed for {UserId}", userId);
            return StatusCode(500, new { Message = "Error updating profile", Error = ex.Message });
        }
    }

    // --------- Helpers ---------
    private static string? GetWarehouseCity1(User? user) => user?.Warehouse?.City;

    private static bool IsAllowed(IFormFile f, bool allowPdf)
    {
        var okExt = allowPdf
            ? new[] { ".png", ".jpg", ".jpeg", ".pdf" }
            : new[] { ".png", ".jpg", ".jpeg" };
        var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
        return okExt.Contains(ext) && f.Length <= 5 * 1024 * 1024; // 5MB
    }

    private async Task<string> SavePrivateAsync(IFormFile file, string subfolder)
    {
        var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "PrivateFiles", "uploads", subfolder);
        Directory.CreateDirectory(baseDir);
        var name = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var path = Path.Combine(baseDir, name);
        await using var fs = System.IO.File.Create(path);
        await file.CopyToAsync(fs);
        return name; // guarda solo nombre/relativo
    }



    private async Task<bool> CheckEmailExistAsync(string email)
    {
        return await _authContext.Users.AnyAsync(u => u.Email == email);
    }



    [Authorize]
    [HttpPut("update/{id:int}")]
    public async Task<IActionResult> UpdateUser([FromRoute] int id, [FromBody] UpdateUserDto dto)
    {
        if (dto == null)
            return BadRequest(new { Message = "Body is required" });

        if (dto.Id.HasValue && dto.Id.Value != id)
            return BadRequest(new { Message = "Route id and body id mismatch" });

        var user = await _authContext.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound(new { Message = "User not found" });

        bool wasInactive = !user.IsActive && (dto.IsActive ?? user.IsActive);

        if (dto.Name != null) user.Name = dto.Name;
        if (dto.LastName != null) user.LastName = dto.LastName;
        if (dto.Email != null) user.Email = dto.Email;
        if (dto.IsActive.HasValue) user.IsActive = dto.IsActive.Value;
        if (dto.IsFirstLogin.HasValue) user.IsFirstLogin = dto.IsFirstLogin.Value;
        if (dto.WasContacted.HasValue) user.WasContacted = dto.WasContacted.Value;
        if (dto.WarehouseId.HasValue) user.WarehouseId = dto.WarehouseId.Value;
        if (dto.UserRole.HasValue) user.UserRole = dto.UserRole.Value;
        if (dto.IdentificationNumber != null) user.IdentificationNumber = dto.IdentificationNumber;
        if (dto.Stage.HasValue) user.Stage = dto.Stage.Value;

        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            user.Password = dto.Password; // Ideal: guardar con hash
        }

        if (dto.InitialDate.HasValue && user.InitialDate != dto.InitialDate.Value)
        {
            user.ConfirmationToken = Guid.NewGuid().ToString("N");
            user.ConfirmationDate = null;
            user.InitialDate = dto.InitialDate.Value;

            var confirmUrl =
                $"https://ttologistics.online/api/Users/confirm-attendance?token={Uri.EscapeDataString(user.ConfirmationToken)}";

            var manager = await _authContext.Users
                .AsNoTracking()
                .Include(x => x.Profile)
                .FirstOrDefaultAsync(x =>
                    x.WarehouseId == user.WarehouseId &&
                    x.UserRole == global::User.Role.Manager);

            var warehouse = await _authContext.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == user.WarehouseId);

            if (manager != null && warehouse != null)
            {
                await _emailService.SendEmailAsync(
                    toEmail: user.Email,
                    subject: "Start Date Confirmation!",
                    "StartDateConfimation.cshtml",
                    placeholders: new()
                    {
                        ["Name"] = user.Name ?? "",
                        ["LastName"] = user.LastName ?? "",
                        ["Position"] = "Driver",
                        ["StartDate"] = dto.InitialDate.Value.ToDateTime(TimeOnly.MinValue)
                            .ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture),
                        ["StartTime"] = $"{warehouse.OpenTime}",
                        ["StartWeekday"] = dto.InitialDate.Value.ToDateTime(TimeOnly.MinValue)
                            .ToString("dddd", System.Globalization.CultureInfo.InvariantCulture),
                        ["WarehouseName"] = $"{warehouse.Company} ({warehouse.City})",
                        ["WarehouseAddress"] = $"{warehouse.Address} {warehouse.City} {warehouse.State}".Trim(),
                        ["ManagerName"] = $"{manager.Name} {manager.LastName}".Trim(),
                        ["ManagerPhone"] = manager.Profile?.PhoneNumber ?? "",
                        ["ManagerEmail"] = manager.Email ?? "",
                        ["ConfirmUrl"] = confirmUrl
                    },
                    copy: false
                );

                await _emailService.SendEmailAsync(
                    toEmail: manager.Email,
                    subject: "Start Date Confirmation!",
                    "HireStartReminder.cshtml",
                    placeholders: new()
                    {
                        ["ManagerName"] = $"{manager.Name} {manager.LastName}".Trim(),
                        ["WarehouseName"] = $"{warehouse.Company} ({warehouse.City})",
                        ["Name"] = user.Name ?? "",
                        ["LastName"] = user.LastName ?? "",
                        ["Position"] = "Driver",
                        ["ApplicantPhone"] = user.Profile?.PhoneNumber ?? "",
                        ["ApplicantEmail"] = user.Email ?? "",
                        ["StartTime"] = $"{warehouse.OpenTime}",
                        ["StartDate"] = dto.InitialDate.Value.ToDateTime(TimeOnly.MinValue)
                            .ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture),
                        ["StartWeekday"] = dto.InitialDate.Value.ToDateTime(TimeOnly.MinValue)
                            .ToString("dddd", System.Globalization.CultureInfo.InvariantCulture)
                    },
                    copy: true
                );
            }
        }

        if (wasInactive && user.IsActive)
        {
          
            var okAccessEmail = await SendAccountActivatedEmail(user, "Account Activated!!");

            if (!okAccessEmail)
                return BadRequest(new { Message = "Access email was not sent." });
        }

        user.UpdatedAt = DateTime.UtcNow;

        await _authContext.SaveChangesAsync();

        return Ok(new { Message = "User updated successfully!" });
    }
    [Authorize]
    [HttpPost("resend-password-setup/{id:int}")]
    public async Task<IActionResult> ResendPasswordSetup([FromRoute] int id)
    {
        var user = await _authContext.Users
      .FirstOrDefaultAsync(x => x.Id == id);

        if (user == null)
            return NotFound(new { Message = "User not found" });

        var ok = await SendAccountActivatedEmail(user, "Setup Your Password");

        if (!ok)
            return BadRequest(new { Message = "Email could not be sent." });

        return Ok(new { Message = "Password setup email resent successfully" });
    }

    [AllowAnonymous]
    [HttpPost("set-password")]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
            return BadRequest(new { Message = "Invalid token." });

        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
            return BadRequest(new { Message = "Password must be at least 6 characters." });

        var user = await _authContext.Users
            .FirstOrDefaultAsync(u =>
                u.PasswordResetToken == dto.Token &&
                u.PasswordResetTokenExpiresAt > DateTime.UtcNow);

        if (user is null)
            return BadRequest(new { Message = "Token expired or invalid." });

        user.Password = PasswordHasher.HashPassword(dto.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;
        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;

        await _authContext.SaveChangesAsync();

        return Ok(new { Message = "Password created successfully." });
    }


    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _authContext.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { Message = "User not found" });

        try
        {
            _authContext.Users.Remove(user);
            await _authContext.SaveChangesAsync();
            return Ok(new { Message = "User deleted successfully!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Message = "Error deleting user", Error = ex.Message });
        }
    }

    private string GetEmailManager(User userObj)
    {
        var user = _authContext.Users
            .FirstOrDefault(u => u.UserRole == global::User.Role.Manager && u.WarehouseId == userObj.WarehouseId);

        return user?.Email?.Trim(); // Si no hay manager, devuelve null
    }
    private string GetWarehouseCity(User userObj)
    {
        var warehouseId = userObj.WarehouseId;

        var warehouse = _authContext.Warehouses
            .FirstOrDefault(w => w.Id == warehouseId);

        return warehouse != null ? $"{warehouse.Company} - {warehouse.City}" : null;
    }

    [Authorize]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("id");
        if (userIdClaim == null)
            return Unauthorized(new { Message = "Invalid token" });

        int userId = int.Parse(userIdClaim.Value);

        var user = await _authContext.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { Message = "User not found" });

        if (!PasswordHasher.VerifiPassword(model.CurrentPassword, user.Password))
            return BadRequest(new { Message = "Current password is incorrect" });

        if (model.NewPassword != model.ConfirmPassword)
            return BadRequest(new { Message = "New password and confirmation do not match" });

        user.Password = PasswordHasher.HashPassword(model.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _authContext.SaveChangesAsync();

        return Ok(new { Message = "Password updated successfully" });
    }

    [Authorize]
    [HttpPost("sendMessageApplicant")]
    public async Task<IActionResult> SendMessageApplicant([FromBody] List<SendMessageApplicantDto> dtos)
    {
        if (dtos == null || !dtos.Any())
            return BadRequest(new { message = "Invalid payload" });

        int sent = 0;
        int skipped = 0;

        foreach (var dto in dtos)
        {
            if (dto.Id <= 0)
            {
                skipped++;
                continue;
            }

            var userInDb = await _authContext.Users
                .Include(u => u.Profile)
                .Include(u => u.Warehouse)
                .FirstOrDefaultAsync(u => u.Id == dto.Id);

            if (userInDb == null)
            {
                skipped++;
                continue;
            }

            var warehouseId = dto.WarehouseId ?? userInDb.WarehouseId;
            if (!warehouseId.HasValue)
            {
                skipped++;
                continue;
            }

            var wmt = await _authContext.WarehouseMessageTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.WarehouseId == warehouseId && w.IsDefault);

            if (wmt == null)
            {
                skipped++;
                continue;
            }

            var warehouse = await _authContext.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == warehouseId);

            if (warehouse == null)
            {
                skipped++;
                continue;
            }

            userInDb.WasContacted = true;
            userInDb.IsActive = true;
            userInDb.UpdatedAt = DateTime.UtcNow;

            var phone = userInDb.Profile?.PhoneNumber;

            if (!string.IsNullOrWhiteSpace(phone))
            {
                _whatsAppService.EnviarMensaje(phone, wmt.MessageBody);
            }

            if (!string.IsNullOrWhiteSpace(userInDb.Email))
            {
                await _emailService.SendEmailAsync(
                    toEmail: userInDb.Email!,
                    subject: "Thank you!!",
                    "FirstContact.cshtml",
                    placeholders: new()
                    {
                        ["body"] = wmt.MessageBody,
                        ["city"] = warehouse.City
                    },
                    copy: true
                );
            }

            sent++;
        }

        await _authContext.SaveChangesAsync();

        return Ok(new
        {
            sent,
            skipped
        });
    }


    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile([FromQuery] int? userId, CancellationToken ct = default)
    {
        // ── Auth y rol
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var currentUserId))
            return Unauthorized();

        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;

        var targetId = userId ?? currentUserId;
        var isSelf = targetId == currentUserId;

        bool isAdminOrAssistant =
            role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("Assistant", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("Assistans", StringComparison.OrdinalIgnoreCase); // si así está guardado

        bool isManager = role.Equals("Manager", StringComparison.OrdinalIgnoreCase);

        // ── Manager consultando a OTRO usuario → básico
        if (!isSelf && isManager)
        {
            var basic = await _authContext.Users
                .AsNoTracking()
                .Include(u => u.Profile)
                .Include(u => u.Warehouse) // sin ThenInclude para evitar el error previo
                .Where(u => u.Id == targetId)
                .Select(u => new
                {
                    id = u.Id,
                    name = u.Name,
                    lastName = u.LastName,
                    email = u.Email,
                    role = u.UserRole.ToString(),
                    avatar = u.AvatarUrl,
                    warehouse = u.Warehouse == null ? null : new
                    {
                        city = u.Warehouse.City,
                        company = u.Warehouse.Company
                    },
                    phone = u.Profile != null ? u.Profile.PhoneNumber : null
                    // Sin datos sensibles
                })
                .FirstOrDefaultAsync(ct);

            return basic is null ? NotFound() : Ok(basic);
        }

        // ── Cualquier OTRO rol consultando a OTRO usuario → 403
        if (!isSelf && !isAdminOrAssistant)
            return Forbid();

        // ── Admin/Assistant consultando a otro → usamos ese Id; si es self o sin userId, queda igual
        if (!isSelf && isAdminOrAssistant)
            currentUserId = targetId;

        // ── Tu consulta original (sin ThenInclude problemático)
        var user = await _authContext.Users
            .Include(u => u.Profile)
            .Include(u => u.Warehouse) // si necesitas más, lo agregas, pero no tocamos el return
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct);

        if (user is null) return NotFound();

        // Vehículo (orden seguro por Id)
        var vehicle = await _authContext.Vehicles
            .AsNoTracking()
            .Where(v => v.UserId == user.Id)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);

        // Cuenta bancaria activa (orden seguro por Id)
        var ActiveAccounts = await _authContext.Accounts
            .AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

        // Última firma (siempre ok)
        var lastSignature = await _authContext.UserDocumentSignatures
            .AsNoTracking()
            .Where(d => d.UserId == user.Id)
            .OrderByDescending(d => d.SignedAtUtc)
            .FirstOrDefaultAsync(ct);

        // Tiene compañía
        var hasCompany = await _authContext.Users
            .AsNoTracking()
            .Where(u => u.Id == user.Id)
            .Select(u => u.CompanyId != null)
            .FirstOrDefaultAsync(ct);

        // ── TU RETURN EXACTO ─────────────────────────────────────────
        return Ok(new
        {
            id = user.Id,
            name = user.Name,
            lastName = user.LastName,
            email = user.Email,
            role = user.UserRole.ToString(),
            avatar = user.AvatarUrl,
            warehouse = user.Warehouse == null ? null : new
            {
                city = user.Warehouse.City,
                company = user.Warehouse.Company
            },
            phone = user?.Profile?.PhoneNumber,
            driverUrl = user?.Profile?.DrivingLicenseUrl,
            ssnn = user?.Profile?.SsnLast4,
            ssnUpdatedAt = user?.Profile?.SsnUpdatedAt,
            ssnUrl = user?.Profile?.SocialSecurityUrl,
            expDriverLicense = user?.Profile?.ExpDriverLicense,
            driverLicenseNumber = user?.Profile?.DriverLicenseNumber,
            vehicle = vehicle?.Make,
            vehicleModel = vehicle?.Model,
            AccountNumber = ActiveAccounts?.AccountNumber,
            RoutingNumber = ActiveAccounts?.RoutingNumber,
            ExpInsurance = user?.Profile?.ExpInsurance,
            InsuranceUrl = user?.Profile?.InsuranceUrl,
            accountId = ActiveAccounts?.Id,
            contractSigned = lastSignature?.SignedPdfUrl,

            hasCompany,

            isFirstLogin = user?.IsFirstLogin
        });
    }
    [HttpGet("{id}/ssn")]
    public async Task<IActionResult> GetSsn(int id)
    {
        // 1) obtener requester desde claims
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null) return Unauthorized();
        int requesterId = int.Parse(userIdClaim);

        // 2) obtener rol y companyId del requester (para validar CompanyOwner)
        var requester = await _authContext.Users
            .AsNoTracking()
            .Where(u => u.Id == requesterId)
            .Select(u => new { u.Id, u.UserRole, u.CompanyId })
            .FirstOrDefaultAsync();

        if (requester == null) return Unauthorized();

        // ajustar los nombres de enum/roles a tu proyecto (esto usa el enum global::User.Role como ejemplo)
        bool isAdmin = requester.UserRole.HasValue && requester.UserRole.Value == global::User.Role.Admin;
        bool isCompanyOwner = requester.UserRole.HasValue && requester.UserRole.Value == global::User.Role.CompanyOwner;

        if (!isAdmin && !isCompanyOwner)
            return Forbid();

        // 3) obtener datos del usuario objetivo (solo los campos necesarios)
        var target = await _authContext.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.CompanyId,
                Profile = u.Profile == null ? null : new
                {
                    u.Profile.SsnLast4,
                    // traemos el campo cifrado tal cual (string). Si en tu model es byte[] ajusta abajo.
                    u.Profile.SsnEncrypted
                }
            })
            .FirstOrDefaultAsync();

        if (target == null) return NotFound();

        // 4) si requester es CompanyOwner, asegurarse que pertenezcan a la misma company
        if (isCompanyOwner && target.CompanyId != requester.CompanyId)
            return Forbid();

        // 5) auditar el acceso (quién, a quién, ip, motivo)
        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
         
        }
        catch (Exception ex)
        {
            // audit failure should not block the main flow, pero logueamos
            _logger.LogWarning(ex, "Audit log failed while accessing SSN");
        }

        // 6) si no hay ssn cifrado, devolvemos la máscara (si existe last4)
        var last4 = target.Profile?.SsnLast4;
        var masked = !string.IsNullOrEmpty(last4) ? $"***-**-{last4}" : "***-**-----";

        if (string.IsNullOrEmpty(target.Profile?.SsnEncrypted))
            return Ok(new { masked });

        // 7) desencriptar con ISensitiveDataProtector (lo hacemos aquí, en el controller)
        string decrypted;
        try
        {
            decrypted = _protector.Unprotect(target.Profile.SsnEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error desencriptando SsnEncrypted para user {UserId}", id);
            return StatusCode(500, "Error al desencriptar el SSN");
        }

        // 8) formatear y devolver solo la máscara + ssn formateado
        return Ok(new
        {
            masked,
            ssn = FormatSsn(decrypted) // ejemplo: 123-45-6789
        });
    }

    // Helper: extraer el user id del claim (ajusta si tu claim usa otro tipo)
    private Guid? GetUserIdFromClaims()
    {
        var idValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst("sub")?.Value;
        if (Guid.TryParse(idValue, out var g)) return g;
        return null;
    }

    private string FormatSsn(string digits)
    {
        if (string.IsNullOrEmpty(digits)) return digits ?? string.Empty;
        var d = new string(digits.Where(char.IsDigit).ToArray());
        if (d.Length == 9) return $"{d.Substring(0, 3)}-{d.Substring(3, 2)}-{d.Substring(5, 4)}";
        return digits;
    }
    [AllowAnonymous]
    [HttpPost("apply-from-twilio")]
    public async Task<IActionResult> ApplyFromTwilio(
    [FromBody] TwilioApplyRequest req,
    CancellationToken ct)
    {
        if (req is null) return BadRequest(new { Message = "Empty payload." });

        var email = req.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { Message = "Email is required." });

        var city = req.City?.Trim();
        if (string.IsNullOrWhiteSpace(city))
            return BadRequest(new { Message = "City is required." });

        // Normalizar teléfono: quitar "whatsapp:" y dejar + y dígitos
        var phoneRaw = (req.Whatsapp ?? string.Empty).Trim();
        if (phoneRaw.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
            phoneRaw = phoneRaw.Substring("whatsapp:".Length);
        string phone = new string(phoneRaw.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (phone.Length > 20) return BadRequest(new { Message = "Phone number too long." });

        // Email único
        var exists = await _authContext.Users.AsNoTracking()
            .AnyAsync(x => x.Email != null && x.Email.ToLower() == email, ct);
        if (exists)
            return BadRequest(new { Message = "Sorry, that email is already in use!" });

        // Buscar Warehouse por ciudad (exacto y luego contains)
        var cityNorm = city.ToLowerInvariant();
        var whInfo = await _authContext.Warehouses.AsNoTracking()
            .Where(w => w.City != null && w.City.ToLower() == cityNorm)
            .Select(w => new { w.Id, w.City, w.CompanyId, w.IsHiring })
            .FirstOrDefaultAsync(ct);

        if (whInfo is null)
        {
            whInfo = await _authContext.Warehouses.AsNoTracking()
                .Where(w => w.City != null && w.City.ToLower().Contains(cityNorm))
                .OrderByDescending(w => w.IsHiring)
                .Select(w => new { w.Id, w.City, w.CompanyId, w.IsHiring })
                .FirstOrDefaultAsync(ct);
        }

        if (whInfo is null)
            return BadRequest(new { Message = $"No warehouse found for city '{city}'." });

        if (!whInfo.CompanyId.HasValue)
            return BadRequest(new { Message = "Warehouse has no company assigned." });

        // Parsear vehículo (Make/Model/Year) desde modelYear
        var pv = ParseModelYear(req.ModelYear);
        if (string.IsNullOrWhiteSpace(pv.Make) || string.IsNullOrWhiteSpace(pv.Model))
            return BadRequest(new { Message = "Invalid vehicle (Make/Model) in 'modelYear'." });

        await using var tx = await _authContext.Database.BeginTransactionAsync(ct);
        try
        {
            // Nombre / Apellido si es posible
            string? first = req.FullName?.Trim();
            string? last = "";
            if (!string.IsNullOrWhiteSpace(req.FullName))
            {
                var parts = req.FullName!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    first = parts[0];
                    last = string.Join(' ', parts.Skip(1));
                }
            }

            // Password temporal aleatoria
            var tempPassword = Guid.NewGuid().ToString("N")[..12] + "!";

            var user = new User
            {
                Name = first,
                LastName = last,
                Email = email,
                Password = PasswordHasher.HashPassword(tempPassword),
                IsActive = false,
                AcceptsSMSNotifications = true,
                CompanyId = whInfo.CompanyId,
                WarehouseId = whInfo.Id,
                UserRole = global::User.Role.Applicant
            };
            await _authContext.Users.AddAsync(user, ct);
            await _authContext.SaveChangesAsync(ct);

            var profile = new UserProfile
            {
                Id = user.Id,
                PhoneNumber = phone
            };
            await _authContext.UserProfiles.AddAsync(profile, ct);
            await _authContext.SaveChangesAsync(ct);

            var vehicle = new Vehicle
            {
                UserId = user.Id,
                Make = pv.Make,
                Model = pv.Model,
                Year = pv.Year,
                Type = req.VehicleType,
                IsDefault = true
            };
            await _authContext.Vehicles.AddAsync(vehicle, ct);
            await _authContext.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            // -------- Emails / notificaciones --------
            var placeholders = new Dictionary<string, string>
        {
            { "Name", user.Name ?? "" },
            { "LastName", user.LastName ?? "" },
            { "Email", user.Email ?? "" },
            { "PhoneNumber", profile.PhoneNumber ?? "" },
            { "Make", vehicle.Make ?? "" },
            { "Model", vehicle.Model ?? "" },
            { "Year", vehicle.Year?.ToString() ?? "" },
            { "VehicleType", vehicle.Type ?? "" },
            { "Locality", whInfo.City ?? "" }
        };

            // 1) Manager del warehouse (si existe lógica para resolverlo)
            var managerEmail = GetEmailManager(user); // tu helper existente
            if (!string.IsNullOrWhiteSpace(managerEmail))
            {
                await _emailService.SendEmailAsync(
                    toEmail: managerEmail,
                    subject: "New driver on the way!",
                    "ApplicationTemplate.cshtml",
                    placeholders: placeholders,
                    copy: false
                );
            }

            // 2) Todos los Admins
            var adminEmails = await _authContext.Users.AsNoTracking()
                .Where(u2 => u2.UserRole == global::User.Role.Admin && !string.IsNullOrEmpty(u2.Email))
                .Select(u2 => u2.Email!)
                .ToListAsync(ct);

            foreach (var emailAdmin in adminEmails)
            {
                await _emailService.SendEmailAsync(
                    toEmail: emailAdmin,
                    subject: "New driver on the way!",
                    "ApplicationTemplate.cshtml",
                    placeholders: placeholders,
                    copy: false
                );
            }

            // 3) Confirmación al aplicante
            var okUserMail = await _emailService.SendEmailAsync(
                toEmail: user.Email!,
                subject: "Thank you",
                "ApplicationConfirmation.cshtml",
                placeholders: placeholders,
                copy: false
            );

            if (!okUserMail)
                return BadRequest(new { Message = "Failed to send confirmation email." });

            // 4) (Opcional) Auto-contacto si el WH contrata
            if (whInfo.IsHiring)
                await _applicantContactService.ContactApplicantAsync(user.Id);

            return Ok(new
            {
                Message = "Thank you for applying with us!",
                responseId = $"APP-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                warehouse = new { whInfo.Id, whInfo.City, whInfo.IsHiring }
            });
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            var inner = ex.InnerException?.Message ?? ex.GetBaseException().Message ?? ex.Message;
            return StatusCode(500, new { Message = "DB update error.", Error = inner });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return StatusCode(500, new { Message = "An error occurred while processing your request.", Error = ex.Message });
        }
    }

    // ---- Helper: parsear "Kia Sorento 2023" -> (Kia, Sorento, 2023)
    private static ParsedVehicle ParseModelYear(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new(null, null, null);

        var s = input.Trim();
        int? year = null;

        // Busca último token como año (4 dígitos 19xx/20xx)
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count >= 2)
        {
            if (int.TryParse(parts[^1], out var y) && y is >= 1900 and <= 2100)
            {
                year = y;
                parts.RemoveAt(parts.Count - 1);
            }
        }

        if (parts.Count == 1)
            return new(parts[0], "", year);

        // Make = primer token, Model = el resto
        var make = parts[0];
        var model = string.Join(' ', parts.Skip(1));

        return new(make, model, year);
    }

    [HttpGet("confirm-attendance")]
    [AllowAnonymous] // importante: este link lo abre alguien no logueado
    public async Task<IActionResult> ConfirmAttendance([FromQuery] string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest("Invalid token.");

        var user = await _authContext.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.ConfirmationToken == token, ct);

        if (user == null)
            return NotFound("Confirmation link is invalid or has expired.");

        // Si ya confirmó antes, puedes decidir qué hacer
        if (user.ConfirmationDate is null)
        {
            user.ConfirmationDate = DateOnly.FromDateTime(DateTime.UtcNow);
            // Opcional: invalidar el token para que sea de un solo uso
            user.ConfirmationToken = null;

            await _authContext.SaveChangesAsync(ct);

            // Notificar a manager y admin
            await NotifyManagersAndAdminsAsync(user, ct);
        }

        // Aquí puedes devolver una vista simple o un mensaje
        // si usas solo API:
        return Content("Thank you! Your attendance has been confirmed.");
    }

    private async Task NotifyManagersAndAdminsAsync(User user, CancellationToken ct)
    {
        // Buscar managers y admins de la misma compañía
        var recipients = await _authContext.Users
            .Where(u => u.CompanyId == user.CompanyId &&
                        (u.UserRole == global::User.Role.Manager || u.UserRole == global::User.Role.Admin) &&
                        u.Email != null)
            .Select(u => u.Email!)
            .ToListAsync(ct);

        if (!recipients.Any())
            return;

        var subject = $"Attendance confirmed - {user.Name} {user.LastName}";
        var body = $@"
            <p>El usuario <strong>{user.Name} {user.LastName}</strong> ha confirmado que asistirá a trabajar.</p>
            <p>Fecha de confirmación: {user.ConfirmationDate:MM/dd/yyyy}</p>
            <p>Compañía: {user.Company?.Name}</p>";

        foreach (var email in recipients)
        {
            await _emailService.SendEmailAsync(email, subject, body);
        }
    }

    [HttpGet("{id:int}/incomplete-fields")]
    public async Task<ActionResult<UserValidationResultDto>> GetIncompleteFields(int id)
    {
        var user = await _authContext.Users
            .Include(u => u.Profile)
            .Include(u => u.DocumentSignatures)
            .Include(u => u.Vehicles)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound();

        var missing = new List<string>();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Campos del User
        if (string.IsNullOrWhiteSpace(user.Name))      missing.Add("Name");
        if (string.IsNullOrWhiteSpace(user.LastName))  missing.Add("Last Name");
        if (string.IsNullOrWhiteSpace(user.Email))     missing.Add("Email");
        //if (string.IsNullOrWhiteSpace(user.AvatarUrl)) missing.Add("AvatarUrl");


        // Profile (puede ser null)
        if (user.Profile == null)
        {
            missing.Add("Profile");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(user.Profile.PhoneNumber))
                missing.Add("Phone Number");
            if ( user.UserRole == global::User.Role.Driver && string.IsNullOrWhiteSpace(user.Profile.DriverLicenseNumber))        
                missing.Add("Driver License Number");
            if (user.UserRole == global::User.Role.Driver && (user.Profile.ExpDriverLicense == null || user.Profile.ExpDriverLicense < today))
                missing.Add("Expired Driver License");
            if (string.IsNullOrWhiteSpace(user.Profile.SsnLast4))
                missing.Add("SSN Last 4");
            if (user.UserRole == global::User.Role.Driver &&string.IsNullOrWhiteSpace(user.Profile.InsuranceUrl))   
                missing.Add("Insurance photo");
            if (user.UserRole == global::User.Role.Driver && (user.Profile.ExpInsurance == null || user.Profile.ExpInsurance < today))
                missing.Add("Expired Insurance");
            if (string.IsNullOrWhiteSpace(user.Profile.SocialSecurityUrl))
                missing.Add("Social Security photo");
            if (user.UserRole == global::User.Role.Driver && (string.IsNullOrWhiteSpace(user.Profile.DrivingLicenseUrl)))
                missing.Add("Driving License photo");   
            if (user.Profile.DateOfBirth == null)
                missing.Add("Date of Birth");
        }
        if ((user.UserRole != global::User.Role.Admin && user.UserRole != global::User.Role.CompanyOwner ) && ((user.DocumentSignatures == null || !user.DocumentSignatures.Any())))
        {
            missing.Add("Document Signatures");
        }


        if ((user.UserRole != global::User.Role.Admin && user.UserRole != global::User.Role.CompanyOwner ) && user.Warehouse == null)
        {
            missing.Add("Warehouse");
        }

        if (user.UserRole == global::User.Role.Driver && (user.Vehicles == null || !user.Vehicles.Any()))
        {
            missing.Add("Vehicles");
        }
        else
        {
            var i = 0;

            foreach (var v in user.Vehicles)
            {
                if (user.UserRole == global::User.Role.Driver && string.IsNullOrWhiteSpace(v.Make))  missing.Add($"Vehicles[{i}].Make");
                if (user.UserRole == global::User.Role.Driver && string.IsNullOrWhiteSpace(v.Model)) missing.Add($"Vehicles[{i}].Model");
                i++;
            }
        }

        var result = new UserValidationResultDto
        {
            UserId = user.Id,
            MissingFields = missing
        };

        return Ok(result);
    }

    // [HttpGet("{userId:int}/daily-summary")]
    // public async Task<IActionResult> GetDailySummary(
    //     int userId,
    //     [FromQuery] DateOnly? from,
    //     [FromQuery] DateOnly? to)
    // {
    //     // 0) Validar usuario + obtener Warehouse.Company
    //     var userInfo = await (
    //         from u in _authContext.Users.AsNoTracking()
    //         join w in _authContext.Warehouses.AsNoTracking()
    //             on u.WarehouseId equals w.Id into wj
    //         from w in wj.DefaultIfEmpty()
    //         where u.Id == userId
    //         select new
    //         {
    //             u.Id,
    //             u.WarehouseId,
    //             WarehouseCompany = w != null ? w.Company : null
    //         }
    //     ).FirstOrDefaultAsync();

    //     if (userInfo is null)
    //         return NotFound($"User {userId} not found.");
        
    //     var userWarehouseId = userInfo.WarehouseId.Value;


    //     if (!userInfo.WarehouseId.HasValue)
    //         return Ok(new List<RouteWithPackagesDto>()); // el usuario no tiene warehouse


    //     var isOnTrac = (userInfo.WarehouseCompany ?? "")
    //         .Trim()
    //         .Equals("ontrac", StringComparison.OrdinalIgnoreCase);

    //     // 1) Fechas opcionales (rango semi-abierto para incluir el último día completo)
    //     DateTime? start = from.HasValue ? from.Value.ToDateTime(TimeOnly.MinValue) : null;
    //     DateTime? endExclusive = to.HasValue ? to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue) : null;

    //     // 2) Si es OnTrac: SOLO rutas (sin paquetes)
    //     if (isOnTrac)
    //     {
    //         var routesOnly = await _authContext.Set<Routes>()
    //             .AsNoTracking()
    //             .Where(r => r.UserId.HasValue &&
    //                 r.UserId.Value == userId &&
    //                 r.WarehouseId.HasValue &&
    //                 r.WarehouseId.Value == userWarehouseId
    //             )
    //             .Where(r => !start.HasValue || r.Date >= start.Value)
    //             .Where(r => !endExclusive.HasValue || r.Date < endExclusive.Value)
    //             .Select(r => new RouteWithPackagesDto
    //             {
    //                 RouteId = r.Id,
    //                 Date = r.Date,
    //                 WarehouseId = r.WarehouseId,
    //                 DeliveryStops = r.DeliveryStops,
    //                 RouteStatus = r.routeStatus,
    //                 Volumen = r.Volumen,
    //                 Los = r.Los,
    //                 CustomerOnTime = r.CustomerOnTime,
    //                 BranchOnTime = r.BranchOnTime,
    //                 CNL = r.CNL,
    //                 Attempts = r.Attempts,
    //                 Packages = new List<PackageDto>()
    //             })
    //             .OrderByDescending(r => r.Date)
    //             .ToListAsync();

    //         var byDay = routesOnly
    //             .GroupBy(r => DateOnly.FromDateTime(r.Date))
    //             .OrderByDescending(g => g.Key)
    //             .Select(g => new UserDailySummaryDto
    //             {
    //                 Date = g.Key,
    //                 Routes = g.OrderByDescending(x => x.Date).ToList()
    //             })
    //             .ToList();

    //         return Ok(byDay);
    //     }


    //     // 3) No OnTrac: rutas + paquetes (LEFT JOIN para incluir rutas sin paquetes)
    //     var flat = await (
    //         from r in _authContext.Set<Routes>().AsNoTracking()
    //         join p in _authContext.Set<Packages>().AsNoTracking()
    //             on r.Id equals p.RoutesId into pj
    //         from p in pj.DefaultIfEmpty()

    //         join z in _authContext.Set<Zone>().AsNoTracking()
    //             on r.ZoneId equals z.Id into zj
    //         from z in zj.DefaultIfEmpty()

    //         where r.UserId.HasValue && r.UserId.Value == userId
    //             && (!start.HasValue || r.Date >= start.Value)
    //             && (!endExclusive.HasValue || r.Date < endExclusive.Value)
    //             && r.WarehouseId.HasValue
    //             && r.WarehouseId.Value == userWarehouseId

    //         select new
    //         {
    //             // Route
    //             RouteId = r.Id,
    //             r.Date,
    //             r.WarehouseId,
    //             r.DeliveryStops,
    //             RouteStatus = r.routeStatus,
    //             r.Volumen,
    //             r.Los,
    //             r.CustomerOnTime,
    //             r.BranchOnTime,
    //             r.CNL,
    //             r.Attempts,
    //             ZoneId = r.ZoneId,
    //             ZoneCode = z != null ? z.ZoneCode : null,

    //             // Package (nullable por LEFT JOIN)
    //             PackageId = (int?)(p != null ? p.Id : null),
    //             Tracking = p != null ? p.Tracking : null,
    //             IncidentDate = (DateTime?)(p != null ? p.IncidentDate : null),
    //             Status = (PackageStatus?)(p != null ? p.Status : null),
    //             ReviewStatus = (ReviewStatus?)(p != null ? p.ReviewStatus : null),

    //             Address = p != null ? p.Address : null,
    //             City = p != null ? p.City : null,
    //             State = p != null ? p.State : null,
    //             ZipCode = p != null ? p.ZipCode : null,

    //             ScanLat = p != null ? p.ScanLat : null,
    //             ScanLon = p != null ? p.ScanLon : null,
    //             AddrLat = p != null ? p.AddrLat : null,
    //             AddrLon = p != null ? p.AddrLon : null,

    //             DaysElapsed = (int?)(p != null ? p.DaysElapsed : null),
    //             RSP = (int?)(p != null ? p.RSP : null)
    //         }
    //     ).ToListAsync();

    //    var routes = flat
    //         .GroupBy(x => new
    //         {
    //             x.RouteId,
    //             x.Date,
    //             x.WarehouseId,
    //             x.DeliveryStops,
    //             x.Volumen,
    //             x.Los,
    //             x.CustomerOnTime,
    //             x.BranchOnTime,
    //             x.CNL,
    //             x.Attempts,
    //             x.RouteStatus,
    //             x.ZoneId,
    //             x.ZoneCode
    //         })
    //         .Select(g => new RouteWithPackagesDto
    //         {
    //             RouteId = g.Key.RouteId,
    //             Date = g.Key.Date,
    //             WarehouseId = g.Key.WarehouseId,
    //             DeliveryStops = g.Key.DeliveryStops,
    //             Volumen = g.Key.Volumen,
    //             Los = g.Key.Los,
    //             CustomerOnTime = g.Key.CustomerOnTime,
    //             BranchOnTime = g.Key.BranchOnTime,
    //             CNL = g.Key.CNL,
    //             Attempts = g.Key.Attempts,
    //             RouteStatus = g.Key.RouteStatus,

    //             Packages = g
    //                 .Where(x => x.PackageId.HasValue)
    //                 .GroupBy(x => x.PackageId!.Value)
    //                 .Select(gg => gg.First())
    //                 .Select(x => new PackageDto
    //                 {
    //                     Id = x.PackageId!.Value,
    //                     Tracking = x.Tracking,
    //                     IncidentDate = x.IncidentDate,
    //                     Status = (x.Status ?? PackageStatus.RD).ToString(),
    //                     ReviewStatus = (x.ReviewStatus ?? ReviewStatus.Open).ToString(),
    //                     Address = x.Address,
    //                     City = x.City,
    //                     State = x.State,
    //                     ZipCode = x.ZipCode,
    //                     ScanLat = x.ScanLat,
    //                     ScanLon = x.ScanLon,
    //                     AddrLat = x.AddrLat,
    //                     AddrLon = x.AddrLon,
    //                     DayElapsed = x.DaysElapsed,
    //                     RSP = x.RSP,
    //                     warehouseID = x.WarehouseId
    //                 })
    //                 .ToList()
    //         })
    //         .OrderByDescending(r => r.Date)
    //         .ToList();

    //     // ✅ agrupar por día (nivel superior)
    //     var resultByDay = routes
    //         .GroupBy(r => DateOnly.FromDateTime(r.Date))
    //         .OrderByDescending(g => g.Key)
    //         .Select(g => new UserDailySummaryDto
    //         {
    //             Date = g.Key,
    //             Routes = g.OrderByDescending(x => x.Date).ToList()
    //         })
    //         .ToList();

    //     return Ok(resultByDay);

    // }

    [HttpGet("{userId:int}/daily-summary")]
    public async Task<IActionResult> GetDailySummary(
        int userId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        // 0) Validar usuario + warehouse + company name
        var userInfo = await (
            from u in _authContext.Users.AsNoTracking()
            join w in _authContext.Warehouses.AsNoTracking()
                on u.WarehouseId equals w.Id into wj
            from w in wj.DefaultIfEmpty()
            where u.Id == userId
            select new
            {
                u.Id,
                u.WarehouseId,
                WarehouseCompany = w != null ? w.Company : null
            }
        ).FirstOrDefaultAsync();

        if (userInfo is null)
            return NotFound($"User {userId} not found.");

        if (!userInfo.WarehouseId.HasValue)
            return Ok(new List<UserDailySummaryDto>());

        var userWarehouseId = userInfo.WarehouseId.Value;

        var isOnTrac = (userInfo.WarehouseCompany ?? "")
            .Trim()
            .Equals("ontrac", StringComparison.OrdinalIgnoreCase);

        // 1) Fechas opcionales (rango semi-abierto)
        DateTime? start = from.HasValue ? from.Value.ToDateTime(TimeOnly.MinValue) : null;
        DateTime? endExclusive = to.HasValue ? to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue) : null;

        // =========================
        // A) ROUTES (+ PACKAGES si aplica)
        // =========================

        List<RouteWithPackagesDto> routes = new();

        if (isOnTrac)
        {
            routes = await (
                from r in _authContext.Set<Routes>().AsNoTracking()
                join z in _authContext.Set<Zone>().AsNoTracking()
                    on r.ZoneId equals z.Id into zj
                from z in zj.DefaultIfEmpty()

                where r.UserId.HasValue && r.UserId.Value == userId
                    && r.WarehouseId.HasValue && r.WarehouseId.Value == userWarehouseId
                    && (!start.HasValue || r.Date >= start.Value)
                    && (!endExclusive.HasValue || r.Date < endExclusive.Value)

                select new RouteWithPackagesDto
                {
                    RouteId = r.Id,
                    Date = r.Date,
                    WarehouseId = r.WarehouseId,
                    DeliveryStops = r.DeliveryStops,
                    Volumen = r.Volumen,
                    Los = r.Los,
                    CustomerOnTime = r.CustomerOnTime,
                    BranchOnTime = r.BranchOnTime,
                    CNL = r.CNL,
                    Attempts = r.Attempts,
                    RouteStatus = r.routeStatus,

                    // ✅ nuevo campo
                    ZoneCode = z != null ? z.ZoneCode : null,

                    Packages = new List<PackageDto>() // vacío (OnTrac / sin paquetes)
                }
            )
            .OrderByDescending(x => x.Date)
            .ToListAsync();

        }
        else
        {
            var flat = await (
                from r in _authContext.Set<Routes>().AsNoTracking()
                join p in _authContext.Set<Packages>().AsNoTracking()
                    on r.Id equals p.RoutesId into pj
                from p in pj.DefaultIfEmpty()
                join z in _authContext.Set<Zone>().AsNoTracking()
                    on r.ZoneId equals z.Id into zj
                from z in zj.DefaultIfEmpty()

                where r.UserId.HasValue && r.UserId.Value == userId
                    && r.WarehouseId.HasValue && r.WarehouseId.Value == userWarehouseId
                    && (!start.HasValue || r.Date >= start.Value)
                    && (!endExclusive.HasValue || r.Date < endExclusive.Value)

                select new
                {
                    // Route
                    RouteId = r.Id,
                    r.Date,
                    r.WarehouseId,
                    r.DeliveryStops,
                    RouteStatus = r.routeStatus,
                    r.Volumen,
                    r.Los,
                    r.CustomerOnTime,
                    r.BranchOnTime,
                    r.CNL,
                    r.Attempts,
                    ZoneCode = z != null ? z.ZoneCode : null,

                    // Package
                    PackageId = (int?)(p != null ? p.Id : null),
                    Tracking = p != null ? p.Tracking : null,
                    IncidentDate = (DateTime?)(p != null ? p.IncidentDate : null),
                    Status = (PackageStatus?)(p != null ? p.Status : null),
                    ReviewStatus = (ReviewStatus?)(p != null ? p.ReviewStatus : null),
                    Address = p != null ? p.Address : null,
                    City = p != null ? p.City : null,
                    State = p != null ? p.State : null,
                    ZipCode = p != null ? p.ZipCode : null,
                    ScanLat = p != null ? p.ScanLat : null,
                    ScanLon = p != null ? p.ScanLon : null,
                    AddrLat = p != null ? p.AddrLat : null,
                    AddrLon = p != null ? p.AddrLon : null,
                    DaysElapsed = (int?)(p != null ? p.DaysElapsed : null),
                    RSP = (int?)(p != null ? p.RSP : null),
                    Weight = (decimal?)(p != null ? p.Weight : null)
                }
            ).ToListAsync();

            routes = flat
                .GroupBy(x => new
                {
                    x.RouteId,
                    x.Date,
                    x.WarehouseId,
                    x.DeliveryStops,
                    x.RouteStatus,
                    x.Volumen,
                    x.Los,
                    x.CustomerOnTime,
                    x.BranchOnTime,
                    x.CNL,
                    x.Attempts,
                    x.ZoneCode
                })
                .Select(g => new RouteWithPackagesDto
                {
                    RouteId = g.Key.RouteId,
                    Date = g.Key.Date,
                    WarehouseId = g.Key.WarehouseId,
                    DeliveryStops = g.Key.DeliveryStops,
                    Volumen = g.Key.Volumen,
                    Los = g.Key.Los,
                    CustomerOnTime = g.Key.CustomerOnTime,
                    BranchOnTime = g.Key.BranchOnTime,
                    CNL = g.Key.CNL,
                    Attempts = g.Key.Attempts,
                    RouteStatus = g.Key.RouteStatus,
                    ZoneCode = g.Key.ZoneCode,
                    Packages = g.Where(x => x.PackageId.HasValue)
                        .GroupBy(x => x.PackageId!.Value)
                        .Select(gg => gg.First())
                        .Select(x => new PackageDto
                        {
                            Id = x.PackageId!.Value,
                            Tracking = x.Tracking,
                            IncidentDate = x.IncidentDate,
                            Status = (x.Status ?? PackageStatus.RD).ToString(),
                            ReviewStatus = (x.ReviewStatus ?? ReviewStatus.Open).ToString(),
                            Address = x.Address,
                            City = x.City,
                            State = x.State,
                            ZipCode = x.ZipCode,
                            ScanLat = x.ScanLat,
                            ScanLon = x.ScanLon,
                            AddrLat = x.AddrLat,
                            AddrLon = x.AddrLon,
                            DayElapsed = x.DaysElapsed,
                            RSP = x.RSP,
                            warehouseID = x.WarehouseId,
                            Weight = x.Weight
                        })
                        .ToList()
                })
                .ToList();
        }

        // =========================
        // B) PAYRUN LINES del usuario (DriverId)
        // =========================

        var payRunLines = await (
            from l in _authContext.PayRunLines.AsNoTracking()
            join pr in _authContext.PayRuns.AsNoTracking()
                on l.PayRunId equals pr.Id
            where pr.DriverId == userId
                && l.RouteDate.HasValue
                && (!start.HasValue || l.RouteDate >= start.Value)
                && (!endExclusive.HasValue || l.RouteDate < endExclusive.Value)
                // && pr.Status == "Completed"
            select new PayRunLineDto
            {
                Id = l.Id,
                PayRunId = l.PayRunId,
                SourceType = l.SourceType,
                SourceId = l.SourceId,
                Description = l.Description,
                Qty = l.Qty,
                Rate = l.Rate,
                Amount = l.Amount,
                Tags = l.Tags,
                RouteDate = l.RouteDate,
                ZoneId = l.ZoneId,
                ZoneArea = l.ZoneArea
            }
        ).ToListAsync();
        // return Ok(new
        //     {
        //         count = payRunLines.Count,
        //         items = payRunLines
        //     });
        // =========================
        // C) MERGE por día (DateOnly)
        // =========================

        // diccionario día -> dto
        var map = new Dictionary<DateOnly, UserDailySummaryDto>();

        void EnsureDay(DateOnly d)
        {
            if (!map.ContainsKey(d))
            {
                map[d] = new UserDailySummaryDto
                {
                    Date = d.ToString("yyyy-MM-dd"),
                    Routes = new List<RouteWithPackagesDto>(),
                    PayRunLines = new List<PayRunLineDto>()
                };
            }
        }

        foreach (var r in routes)
        {
            var day = DateOnly.FromDateTime(r.Date);
            EnsureDay(day);
            map[day].Routes.Add(r);
        }

        foreach (var l in payRunLines)
        {
            var day = DateOnly.FromDateTime(l.RouteDate!.Value);
            EnsureDay(day);
            map[day].PayRunLines.Add(l);
        }

       // ordenar rutas y líneas dentro de cada día (opcional pero recomendado)
        foreach (var d in map.Values)
        {
            d.Routes = d.Routes.OrderByDescending(x => x.Date).ToList();
            d.PayRunLines = d.PayRunLines
                .OrderByDescending(x => x.RouteDate)
                .ThenBy(x => x.Id)
                .ToList();
        }

        // ordenar días (más reciente primero)
        var result = map
            .OrderByDescending(kvp => kvp.Key)
            .Select(kvp => kvp.Value)
            .ToList();

        return Ok(result);
    }

    [Authorize(Roles = "Admin,CompanyOwner,Manager,Assistant,Recruiter")]
    [HttpPut("applicants/bulk-warehouse")]
    public async Task<IActionResult> BulkUpdateApplicantsWarehouse([FromBody] BulkUpdateWarehouseDto dto)
    {
        if (dto.ApplicantIds == null || dto.ApplicantIds.Count == 0)
            return BadRequest(new { message = "No applicants selected." });

        if (dto.WarehouseId <= 0)
            return BadRequest(new { message = "Invalid warehouse." });

        var warehouseExists = await _authContext.Warehouses
            .AnyAsync(w => w.Id == dto.WarehouseId);

        if (!warehouseExists)
            return NotFound(new { message = "Warehouse not found." });

        var applicants = await _authContext.Users
            .Where(u => dto.ApplicantIds.Contains(u.Id))
            .Where(u => u.UserRole == global::User.Role.Applicant)
            .ToListAsync();

        if (!applicants.Any())
            return NotFound(new { message = "No valid applicants found." });

        foreach (var applicant in applicants)
        {
            applicant.WarehouseId = dto.WarehouseId;
        }

        await _authContext.SaveChangesAsync();

        return Ok(new
        {
            message = $"Warehouse updated for {applicants.Count} applicants.",
            updated = applicants.Count
        });
    }

    private async Task<bool> SendAccountActivatedEmail(User user, string subject)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return false;

        var companyName = await _authContext.Companies
            .Where(c => c.Id == user.CompanyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync();

        var token = Guid.NewGuid().ToString("N");

        user.PasswordResetToken = token;
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(24);
        user.UpdatedAt = DateTime.UtcNow;

        await _authContext.SaveChangesAsync();

        var resetLink =
            $"https://admin.ttologistics.com/authentication/set-password?token={Uri.EscapeDataString(token)}";

        return await _emailService.SendEmailAsync(
            toEmail: user.Email,
            subject: subject,
            "AccountActivated.cshtml",
            placeholders: new Dictionary<string, string>
            {
                ["Name"] = user.Name ?? "",
                ["LastName"] = user.LastName ?? "",
                ["Email"] = user.Email ?? "",
                ["ResetLink"] = resetLink,
                ["CompanyName"] = companyName ?? "TTO Hub"
            },
            copy: false
        );
    }

}

public class BulkUpdateWarehouseDto
{
    public List<int> ApplicantIds { get; set; } = new();
    public int WarehouseId { get; set; }
}

public class TwilioApplyRequest
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Whatsapp { get; set; }         // e.g. "whatsapp:+1346..."
    public string? VehicleType { get; set; }      // "SUV" | "Minivan" | "Cargo Van"...
    public string? ModelYear { get; set; }        // e.g. "Kia Sorento 2023"
}

public record ParsedVehicle(string? Make, string? Model, int? Year);
public class AvatarUploadDto
{
    [Required]
    [DataType(DataType.Upload)]
    public IFormFile Avatar { get; set; }
}
public class ChangePasswordDto
{
    public string CurrentPassword { get; set; }
    public string NewPassword { get; set; }
    public string ConfirmPassword { get; set; }
}
public class RegisterUserDto
{
    public string Email { get; set; }
    public string Password { get; set; } 
    public string Name { get; set; } 
    public string Lastname { get; set; }
    // Agrega aquí otros campos que necesites mapear a User (FirstName, LastName, etc.)
    public string? ReferralCode { get; set; }
}
public class UserInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }   
    public string Phone {  get; set; }
    public string Avatar {  get; set; }
    public string BankAccount {  get; set; }
    public string RoutingNumber {  get; set; }
    public List<VehicleDto> Vehicles { get; set; }
    public WarehouseDTO? Warehouse { get; set; }
}

public class VehicleDto
{
    public int Id { get; set; }
    public string Make { get; set; }
    public string Model { get; set; }
}
public sealed class TimeDto
{
    public int Hours { get; set; }   // viene como "hours" del front
    public int Minutes { get; set; } // viene como "minutes" del front
}
public class WarehouseDto
{
    public int Id { get; set; }
    public string City { get; set; }
    public string Company {  get; set; }
    public string Address { get; set; }
    public string State { get; set; }
    public bool SendPayroll { get; set; }
    public bool isHiring    { get; set; }
    public int CompanyId   { get; set; }
    public string ZipCode { get; set; }
    public TimeDto? OpenTime { get; set; }
    public List<AuthorizedPersonDto>? AuthorizedPersons { get; set; }
    public int? MetroId { get; set; }
    public Metro? Metro { get; set; }
    public decimal? DriveRate { get; set; }
    public string? FacilityCode { get; set; }
    public string? Manager { get; set; }
    public bool? IsActive { get; set; }
}
public sealed class WarehouseUpsertDto
{
    
    public int Id { get; set; }
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Address { get; set; } = "";
    public string Company { get; set; } = "";
    public string? ZipCode { get; set; }
    public bool IsHiring { get; set; }
    public bool SendPayroll { get; set; }
    public TimeDto? OpenTime { get; set; } // {hours, minutes}
    public List<AuthorizedPersonDto>? AuthorizedPersons { get; set; }
    public int? MetroId { get; set; }
    public decimal? DriveRate { get; set; }
    public string? FacilityCode { get; set; }

}
public class AuthorizedPersonDto
{
    public int Id { get; set; }        // 👈 este es el UserId
    public string Name { get; set; } = null!;
    public string LastName { get; set; } = null!;
}
public sealed class DriverApplicationRequest
{
    [Required] public ApplicantUserDto User { get; set; } = default!;
    [Required] public ApplicantVehicleDto Vehicle { get; set; } = default!;
}

public sealed class ApplicantUserDto
{
    [Required] public string Name { get; set; } = "";
    [Required] public string LastName { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string Password { get; set; } = "";
    public int? metroId { get; set; }
    public string? PhoneNumber { get; set; }
    public bool AcceptsSMSNotifications { get; set; }
   
}

public sealed class ApplicantVehicleDto
{
    [Required] public string Make { get; set; } = "";
    [Required] public string Model { get; set; } = "";
}
public class UpdateUserDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsFirstLogin { get; set; }
    public bool? WasContacted { get; set; }
    public int? WarehouseId { get; set; }
    public string? IdentificationNumber { get; set; }
    public global::User.Role? UserRole { get; set; }
    public string? Password { get; set; } // si permites cambiarla
    public HiringStage? Stage { get; set; }
    public DateOnly? InitialDate {  get; set; }
}

public class SendMessageApplicantDto
{
    public int Id { get; set; }                 // applicantId
    public int? WarehouseId { get; set; }       // opcional: forzar almacén
   // public string? Channel { get; set; }        // "email" | "whatsapp" | ambos (opcional)
}

public class UserValidationResultDto
{
    public int UserId { get; set; }
    public int MissingCount => MissingFields?.Count ?? 0;
    public List<string> MissingFields { get; set; } = new();
}
// public sealed class PackageDto
// {
//     public int Id { get; set; }
//     public string Tracking { get; set; } = "";
//     public TToApp.Model.PackageStatus Status { get; set; }
//     public DateTime IncidentDate { get; set; }
// }
public class SetPasswordDto
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
public sealed class RouteWithPackagesDto
{
    public int RouteId { get; set; }
    public DateTime Date { get; set; }
    public int? WarehouseId { get; set; }
    public int DeliveryStops { get; set; }
    public int Volumen { get; set; }
    public double Los { get; set; }
    public double CustomerOnTime { get; set; }
    public double BranchOnTime { get; set; }
    public int CNL { get; set; }
    public int Attempts { get; set; }
    public  string? ZoneCode { get; set; }
    public TToApp.Model.RouteStatus? RouteStatus { get; set; }
    public List<PackageDto> Packages { get; set; } = new();
}

public sealed class UserDailySummaryDto
{
    public string Date { get; set; } = null!; // "yyyy-MM-dd"
    public List<RouteWithPackagesDto> Routes { get; set; } = new();
    public List<PayRunLineDto> PayRunLines { get; set; } = new();
    // Flags / Conteos
    public bool HasRoutes => Routes.Count > 0;
    public bool HasPayroll => PayRunLines.Count > 0;
    public int RoutesCount => Routes.Count;
    public int PayRunLinesCount => PayRunLines.Count;

    // ✅ Totales por día (Routes)
    public int RoutesTotalStops => Routes.Sum(r => r.DeliveryStops);
    public int RoutesCNL => Routes.Sum(r => r.CNL);
    public int PackagesTotal => Routes.Sum(r => r.Packages?.Count ?? 0);

    // (Opcional) Delivered stops del día
    public int RoutesDeliveredStops => Routes.Sum(r => Math.Max(0, r.DeliveryStops - r.CNL));

    // ✅ Totales por día (Payroll)
    public decimal PayrollTotal => PayRunLines.Sum(x => x.Amount);
    public decimal PayrollPositive => PayRunLines.Where(x => x.Amount > 0).Sum(x => x.Amount);
    public decimal PayrollNegative => PayRunLines.Where(x => x.Amount < 0).Sum(x => x.Amount);

    public Dictionary<string, decimal> PayrollBySourceType =>
        PayRunLines
            .GroupBy(x => x.SourceType ?? "")
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
}

public sealed class RecoverByPhoneDto
{
    public string PhoneNumber { get; set; } = null!;
    public string Password { get; set; } = null!;
}



