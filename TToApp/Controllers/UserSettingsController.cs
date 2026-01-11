using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TToApp.DTOs;
using TToApp.Services.Settings;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserSettingsController : ControllerBase
    {
        private readonly IUserUiSettingsService _svc;
        public UserSettingsController(IUserUiSettingsService svc) => _svc = svc;

        private int GetUserId() =>
            int.Parse(User.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);

        [HttpGet("me")]
        public async Task<ActionResult<UserUiSettingsDTO>> GetMySettings()
            => Ok(await _svc.GetForUserAsync(GetUserId()));

        [HttpPut("me")]
        public async Task<ActionResult<UserUiSettingsDTO>> UpdateMySettings([FromBody] UserUiSettingsDTO dto)
            => Ok(await _svc.UpsertAsync(GetUserId(), dto));
    }
}
