using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TToApp.Services.Sms;

namespace TToApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SmsController : ControllerBase
    {
        private readonly ITtoSmsService _smsService;
        private readonly ILogger<SmsController> _logger;

        public SmsController(
            ITtoSmsService smsService,
            ILogger<SmsController> logger)
        {
            _smsService = smsService;
            _logger = logger;
        }

        [HttpGet("health")]
        public async Task<IActionResult> Health(
            CancellationToken cancellationToken)
        {
            var healthy = await _smsService.HealthAsync(
                cancellationToken);

            return Ok(new
            {
                status = healthy ? "ok" : "unavailable"
            });
        }

        [HttpPost("send")]
        [Authorize(
            Roles = "Admin,CompanyOwner,Manager,Assistant,Recruiter")]
        public async Task<IActionResult> Send(
            [FromBody] SendSmsRequest request,
            CancellationToken cancellationToken)
        {
            if (request is null)
            {
                return BadRequest(new
                {
                    message = "Request is required."
                });
            }

            if (string.IsNullOrWhiteSpace(request.Kind))
            {
                return BadRequest(new
                {
                    message = "Template kind is required."
                });
            }

            if (string.IsNullOrWhiteSpace(request.To))
            {
                return BadRequest(new
                {
                    message = "Phone number is required."
                });
            }

            request.Lang =
                request.Lang?.Trim().ToLowerInvariant() == "es"
                    ? "es"
                    : "en";

            if (string.IsNullOrWhiteSpace(request.ExternalId))
            {
                request.ExternalId =
                    $"tto-sms-{Guid.NewGuid():N}";
            }

            request.Vars ??=
                new Dictionary<string, string>();

            try
            {
                var response = await _smsService.SendAsync(
                    request,
                    cancellationToken);

                return Ok(response);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "Error sending SMS to {Phone}. Kind: {Kind}",
                    request.To,
                    request.Kind);

                return StatusCode(
                    502,
                    new
                    {
                        message = "The SMS engine could not send the message.",
                        detail = ex.Message
                    });
            }
        }
    }
}