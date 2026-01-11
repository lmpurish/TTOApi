using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TToApp.Model;

public class AuthorizePrivateFileAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _paramName;

    public AuthorizePrivateFileAttribute(string paramName = "filename")
    {
        _paramName = paramName;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var db = (ApplicationDbContext)context.HttpContext.RequestServices.GetService(typeof(ApplicationDbContext));
        var userIdClaim = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userIdClaim))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!context.RouteData.Values.TryGetValue(_paramName, out var fileObj))
        {
            context.Result = new BadRequestObjectResult($"Missing required parameter '{_paramName}'.");
            return;
        }

        // 🔒 Normaliza y evita traversal
        var rawFilename = fileObj?.ToString();
        var filename = string.IsNullOrWhiteSpace(rawFilename) ? null : Path.GetFileName(rawFilename);
        if (string.IsNullOrWhiteSpace(filename))
        {
            context.Result = new BadRequestObjectResult("Invalid file name.");
            return;
        }

        var userId = int.Parse(userIdClaim);
        var user = await db.Users
            .Include(u => u.Profile)
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // 🔹 Admin / CompanyOwner pasan siempre (igual que tu lógica previa)
        bool isAdmin = user.UserRole == User.Role.Admin;
        bool isCompanyOwner = user.UserRole == User.Role.CompanyOwner;
        if (isAdmin || isCompanyOwner)
        {
            return; // ✅ acceso permitido
        }

        // 🔹 Compara contra TODOS los archivos del perfil (SSN, DriverLicense, Insurance, Avatar opcional)
        var userFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIf(string? v)
        {
            if (!string.IsNullOrWhiteSpace(v))
                userFiles.Add(Path.GetFileName(v));
        }

        AddIf(user.Profile?.SocialSecurityUrl);
        AddIf(user.Profile?.DrivingLicenseUrl);   // asegúrate del nombre real de la propiedad
        AddIf(user.Profile?.InsuranceUrl);
        AddIf(user.AvatarUrl);          // opcional

        bool isOwner = userFiles.Contains(filename);

        if (!isOwner)
        {
            context.Result = new ForbidResult();
            return;
        }

        // ✅ permitido
    }
}
