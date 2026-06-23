
namespace TToApp.DTOs
{
 public sealed class DriverStoppedWorkingDto
        {
            public long DriverId { get; set; }
            public string DriverName { get; set; } = null!;
            public string? LastRouteDate { get; set; }
            public int DaysSinceLastRoute { get; set; }
        }
}