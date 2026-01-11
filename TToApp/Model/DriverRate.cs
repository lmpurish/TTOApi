namespace TToApp.Model
{
    public class DriverRate
    {
        public long Id { get; set; }
        public long DriverId { get; set; }
        public string RateType { get; set; } = null!; // PerRoute|PerStop|PerPackage|PerMile|Hourly|Mixed
        public decimal BaseAmount { get; set; }
        public decimal? MinPayPerRoute { get; set; }
        public int? OverStopBonusThreshold { get; set; }
        public decimal? OverStopBonusPerStop { get; set; }
        public decimal? FailedStopPenalty { get; set; }
        public decimal? RescueStopRate { get; set; }
        public decimal? NightDeliveryBonus { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
