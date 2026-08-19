using TToApp.Model;

public class DriverPunchAdminRowDto
{
    public long Id { get; set; }

    public int DriverId { get; set; }
    public string DriverName { get; set; } = "";

    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";

    public PunchType PunchType { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public double? DistanceMeters { get; set; }

    public bool IsWithinGeofence { get; set; }

    public PunchSource Source { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}