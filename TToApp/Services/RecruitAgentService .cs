using TToApp.Model;

namespace TToApp.Services
{
    public interface IRecruitAgentService
    {
        Task SendApplicantSmsAsync(User user, UserProfile profile, string locality, CancellationToken ct);
    }
    public class RecruitAgentService : IRecruitAgentService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<RecruitAgentService> _logger;

        public RecruitAgentService(
            HttpClient http,
            IConfiguration config,
            ILogger<RecruitAgentService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        public async Task SendApplicantSmsAsync(
            User user,
            UserProfile profile,
            string locality,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(profile.PhoneNumber))
                return;

            var req = new RecruitSendRequest
            {
                Kind = "onboarding_step",
                To = profile.PhoneNumber,
                Lang = "en",
                ExternalId = $"applicant-{user.Id}-welcome",
                Vars = new Dictionary<string, string>
            {
                { "name", user.Name ?? "" },
                { "lastName", user.LastName ?? "" },
                { "email", user.Email ?? "" },
                { "phone", profile.PhoneNumber ?? "" },
                { "market", locality ?? "" },
                { "link", "https://ttologistics.com" }
            }
            };

            var response = await _http.PostAsJsonAsync("/api/v1/send", req, ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Recruit Agent SMS failed for applicant {UserId}. Status: {Status}. Body: {Body}",
                    user.Id,
                    response.StatusCode,
                    body
                );
                return;
            }

            _logger.LogInformation(
                "Recruit Agent SMS response for applicant {UserId}: {Body}",
                user.Id,
                body
            );
        }
    }
}
