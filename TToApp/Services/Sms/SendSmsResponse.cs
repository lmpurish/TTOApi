using System.Text.Json.Serialization;

namespace TToApp.Services.Sms
{
    public class SendSmsResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("idempotent")]
        public bool Idempotent { get; set; }
    }
}