using System.Net.Http.Json;
using System.Text.Json;

namespace TToApp.Services.Sms
{
    public class TtoSmsService : ITtoSmsService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TtoSmsService> _logger;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        public TtoSmsService(
            HttpClient httpClient,
            ILogger<TtoSmsService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<bool> HealthAsync(
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.GetAsync(
                "/api/v1/health",
                cancellationToken);

            return response.IsSuccessStatusCode;
        }

        public async Task<SendSmsResponse> SendAsync(
            SendSmsRequest request,
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "/api/v1/send",
                request,
                JsonOptions,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "TTO SMS Engine error. Status: {Status}. Body: {Body}",
                    (int)response.StatusCode,
                    body);

                throw new HttpRequestException(
                    $"TTO SMS Engine returned {(int)response.StatusCode}: {body}",
                    null,
                    response.StatusCode);
            }

            var result = JsonSerializer.Deserialize<SendSmsResponse>(
                body,
                JsonOptions);

            if (result is null)
            {
                throw new InvalidOperationException(
                    "The SMS engine returned an invalid response.");
            }

            return result;
        }
    }
}