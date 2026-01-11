using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using TToApp.Model;

namespace TToApp.Security
{
    public class AuthorizeSSNAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string _paramName;

        public AuthorizeSSNAttribute(string paramName = "filename")
        {
            _paramName = paramName;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var db = context.HttpContext.RequestServices.GetService(typeof(ApplicationDbContext)) as ApplicationDbContext;
            var userIdClaim = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userIdClaim))
            {
                context.Result = new UnauthorizedResult();
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

            // 🔹 Captura el parámetro del request (ej: filename)
            var routeValues = context.RouteData.Values;
            if (!routeValues.TryGetValue(_paramName, out var fileObj))
            {
                context.Result = new BadRequestObjectResult($"Missing required parameter '{_paramName}'.");
                return;
            }

            var filename = fileObj?.ToString();

            // 🔹 Reglas de acceso
            bool isOwner = user.Profile?.SocialSecurityUrl == filename;
            bool isAdmin = user.UserRole == User.Role.Admin;
            bool isCompanyOwner = user.UserRole == User.Role.CompanyOwner;

            if (!(isOwner || isAdmin || isCompanyOwner))
            {
                context.Result = new ForbidResult();
                return;
            }

            // ✅ Si llega aquí, tiene permiso
        }
    }
}
