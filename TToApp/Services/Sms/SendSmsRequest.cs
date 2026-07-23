using System.Text.Json.Serialization;

namespace TToApp.Services.Sms
{
    public class SendSmsRequest
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("to")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("lang")]
        public string Lang { get; set; } = "en";

        [JsonPropertyName("externalId")]
        public string ExternalId { get; set; } = string.Empty;

        [JsonPropertyName("vars")]
        public Dictionary<string, string> Vars { get; set; } = new();
    }
}