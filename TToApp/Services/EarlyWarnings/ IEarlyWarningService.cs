namespace TToApp.Services.EarlyWarnings
{
    public interface IEarlyWarningService
    {
        Task CheckHiringCapacityAsync(DateOnly? referenceDate);
       // Task<List<EarlyWarningDebugResult>> CheckHiringCapacityDebugAsync(DateOnly? referenceDate);
        Task CheckMissingDailyPackagesAsync(DateOnly referenceDate);
    }
}