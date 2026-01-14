namespace TToApp.Model
{
    public enum PunchType
    {
        Arrival = 1,
        Departure = 2
    }

    public enum PunchSource
    {
        GPS = 1,
        Manual = 2,
        AdminOverride = 3
    }

    public class DriverPunch
    {
        public long Id { get; set; }

        public int CompanyId { get; set; }
        public int WarehouseId { get; set; }
        public int DriverId { get; set; }

        public PunchType PunchType { get; set; }

        public DateTime OccurredAtUtc { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? AccuracyMeters { get; set; }

        public double DistanceMeters { get; set; }
        public bool IsWithinGeofence { get; set; }

        public PunchSource Source { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
