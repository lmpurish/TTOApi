namespace TToApp.Services.Sms
{
    public interface ITtoSmsService
    {
        Task<bool> HealthAsync(
            CancellationToken cancellationToken = default);

        Task<SendSmsResponse> SendAsync(
            SendSmsRequest request,
            CancellationToken cancellationToken = default);
    }
}