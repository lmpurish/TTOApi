using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace TToApp.Controllers
{
    public abstract class BaseApiController : ControllerBase
    {
        protected bool IsSuperAdmin() =>
            User.FindFirst(ClaimTypes.Role)?.Value == "SuperAdmin";

        protected long GetUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(claim, out var id) ? id : 0;
        }

        // SuperAdmin can pass ?companyId= to target any company.
        // Regular users always get their own companyId from the JWT.
        protected long GetCompanyId(long? superAdminOverride = null)
        {
            if (IsSuperAdmin() && superAdminOverride.HasValue && superAdminOverride.Value > 0)
                return superAdminOverride.Value;

            var claim = User.FindFirst("CompanyId")?.Value ?? User.FindFirst("companyId")?.Value;
            return long.TryParse(claim, out var id) ? id : 0;
        }

        // Returns null when caller is SuperAdmin with no override (means: no company filter, see all).
        protected long? GetCompanyIdOrNull(long? superAdminOverride = null)
        {
            if (IsSuperAdmin())
                return superAdminOverride.HasValue && superAdminOverride.Value > 0
                    ? superAdminOverride
                    : null;

            var claim = User.FindFirst("CompanyId")?.Value ?? User.FindFirst("companyId")?.Value;
            return long.TryParse(claim, out var id) && id > 0 ? id : null;
        }
    }
}
