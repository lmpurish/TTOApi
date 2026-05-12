namespace TToApp.Services.EarlyWarnings
{
    public interface IEarlyWarningNotificationService
    {
        Task NotifyPendingHiringWarningsAsync();
        Task NotifyPendingMissingPackagesWarningsAsync();
    }
}