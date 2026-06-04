using Microsoft.AspNetCore.Mvc;
using TToApp.Services.Payroll;
using TToApp.Services.EarlyWarnings;

namespace TToApp.Controllers;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    [HttpPost("payrun/{driverId:long}/send-latest-approved")]
    public async Task<IActionResult> SendLatestApprovedPayRun(
        long driverId,
        [FromServices] PayRunApprovedSender sender)
    {
        await sender.SendLatestPayRunLineAsync(driverId);

        return Ok(new { ok = true, driverId });
    }

    [HttpPost("test-early-warning")]
    public async Task<IActionResult> TestEarlyWarning()
    {
        var service = HttpContext.RequestServices
            .GetRequiredService<IEarlyWarningService>();

        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));

        await service.CheckHiringCapacityAsync(yesterday);

        return Ok("Early warnings ejecutado");
    }
//     [HttpGet("debug-hiring-capacity")]
// public async Task<IActionResult> DebugHiringCapacity([FromQuery] DateOnly? date)
// {
    
//      var service = HttpContext.RequestServices
//             .GetRequiredService<IEarlyWarningService>();

//     var result = await service.CheckHiringCapacityDebugAsync(date);

//     return Ok(result);
// }
}