using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Twilio;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace TToApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WhatsappController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

        public WhatsappController(IConfiguration cfg) => _cfg = cfg;
        [HttpPost("whatsapp-webhook")]
        public async Task<IActionResult> ReceiveMessage([FromForm] TwilioMessageDto msg, [FromQuery] bool echo = false)
        {
            if (msg is null || string.IsNullOrWhiteSpace(msg.Body) || string.IsNullOrWhiteSpace(msg.From))
                return Ok();

            var twilioSid = _cfg["Twilio:AccountSid"];
            var twilioAuth = _cfg["Twilio:AuthToken"];
            var msSid = _cfg["Twilio:MessagingServiceSid"];     // usa MSID o usa from: msg.To
            var openAiKey = _cfg["OpenAI:ApiKey"];                  // Asegúrate que en appsettings es "OpenAI"

            // --- 1) Llamada a OpenAI con DIAGNÓSTICO ---
            _http.DefaultRequestHeaders.Remove("Authorization");
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiKey}");

            var chatBody = new
            {
                model = "gpt-4o-mini",
                temperature = 0.4,
                max_tokens = 300,
                messages = new[]
                {
            new { role = "system", content = "You are a helpful assistant for TTO Logistics. Be concise and bilingual (ES/EN)." },
            new { role = "user", content = msg.Body.Trim() }
        }
            };

            var req = JsonContent.Create(chatBody);
            var resp = await _http.PostAsync("https://api.openai.com/v1/chat/completions", req);
            var raw = await resp.Content.ReadAsStringAsync(); // <-- clave para ver qué pasó

            string reply;

            if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(raw))
            {
                // Muestra el motivo REAL (temporal para depurar)
                reply = $"[AI ERROR {((int)resp.StatusCode)}] {raw}";
            }
            else
            {
                // Parseo robusto (por si cambia el casing)
                OpenAiResponse? ai;
                try
                {
                    ai = System.Text.Json.JsonSerializer.Deserialize<OpenAiResponse>(
                        raw,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    ai = null;
                }

                reply = ai?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
                if (string.IsNullOrWhiteSpace(reply))
                    reply = "Sorry, I didn’t understand. Can you rephrase? / ¿Podrías reformular tu mensaje?";
            }

            // --- 2) Modo ECO (para Swagger/Postman): devuelve el texto y el raw de OpenAI ---
            if (echo) return Ok(new { reply, openai_raw = raw });

            // --- 3) Envío por WhatsApp ---
            try
            {
                TwilioClient.Init(twilioSid, twilioAuth);

                // a) Usando Messaging Service (recomendado)
                if (!string.IsNullOrWhiteSpace(msSid))
                {
                    await MessageResource.CreateAsync(
                        messagingServiceSid: msSid,
                        to: new Twilio.Types.PhoneNumber(msg.From),
                        body: reply
                    );
                }
                else
                {
                    // b) O responde desde el número que recibió (sin MSID)
                    await MessageResource.CreateAsync(
                        from: new Twilio.Types.PhoneNumber(msg.To),
                        to: new Twilio.Types.PhoneNumber(msg.From),
                        body: reply
                    );
                }
            }
            catch (Exception ex)
            {
                // último recurso
                try
                {
                    await MessageResource.CreateAsync(
                        messagingServiceSid: msSid,
                        to: new Twilio.Types.PhoneNumber(msg.From),
                        body: "Lo siento, tenemos un inconveniente momentáneo. Intenta de nuevo en unos minutos. 🙏"
                    );
                }
                catch { /* swallow */ }
            }

            return Ok();
        }

        /*    [HttpPost("whatsapp-studio-gpt")]
            public async Task<IActionResult> StudioGpt([FromBody] GptWebhookDto dto)
            {
                if (dto is null || string.IsNullOrWhiteSpace(dto.Body))
                    return BadRequest(new { error = "empty_body" });

                var openAiKey = _cfg["OpenAI:ApiKey"];
                if (string.IsNullOrWhiteSpace(openAiKey))
                    return StatusCode(500, new { error = "missing_openai_key" });

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openAiKey);

                var chatBody = new
                {
                    model = "gpt-4o-mini",
                    temperature = 0.4,
                    messages = new[]
                    {
                new { role = "system", content = "You are a helpful assistant for TTO Logistics. Be concise and bilingual (ES/EN)." },
                new { role = "user", content = dto.Body.Trim() }
            }
                };

                var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions",
                    System.Net.Http.Json.JsonContent.Create(chatBody));

                var raw = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, new { error = "openai", raw });

                var ai = System.Text.Json.JsonSerializer.Deserialize<OpenAiResponse>(
                    raw, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var reply = ai?.Choices?.FirstOrDefault()?.Message?.Content?.Trim()
                            ?? "¿Podrías reformular tu mensaje?";

                // Studio espera JSON válido
                return Ok(new { reply });
            }
        */
        [HttpPost("whatsapp-studio-gpt")]
        public async Task<IActionResult> StudioGpt([FromBody] GptWebhookDto dto, [FromServices] IMemoryCache cache)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Body))
                return BadRequest(new { error = "empty_body" });

            var sessionId = string.IsNullOrWhiteSpace(dto.SessionId) ? dto.From : dto.SessionId;

            // 1) Cargar historial (lista de mensajes OpenAI)
            var history = cache.GetOrCreate($"chat:{sessionId}", entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromHours(2); // se renueva con actividad
                return new List<object>();                       // mensajes acumulados
            });

            // 2) Añadir el turno del usuario
            history.Add(new { role = "user", content = dto.Body.Trim() });

            // 3) Construir prompt con system + últimos N mensajes (p. ej. 10)
            var lastTurns = history.Count > 20 ? history.TakeLast(20).ToList() : history;

            var chatBody = new
            {
                model = "gpt-4o-mini",
                temperature = 0.4,
                messages = new List<object>
        {
            new { role = "system", content =
                "You are a helpful assistant for TTO Logistics. Be concise and bilingual (ES/EN). Keep answers under 120 words unless asked otherwise." }
        }.Concat(lastTurns).ToList()
            };

            // 4) Llamar a OpenAI
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _cfg["OpenAI:ApiKey"]);

            var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions",
                            System.Net.Http.Json.JsonContent.Create(chatBody));
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, new { error = "openai", raw });

            var ai = System.Text.Json.JsonSerializer.Deserialize<OpenAiResponse>(
                raw, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var reply = ai?.Choices?.FirstOrDefault()?.Message?.Content?.Trim()
                        ?? "¿Podrías reformular tu mensaje?";

            // 5) Guardar la respuesta del asistente en el historial
            history.Add(new { role = "assistant", content = reply });

            // 6) (Higiene) Limitar tamaño del historial
            if (history.Count > 50) history.RemoveRange(0, history.Count - 50);

            // 7) Devolver a Studio
            return Ok(new { reply });
        }


    }
    public class GptWebhookDto
    {
        public string Body { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string SessionId { get; set; } // <- NUEVO
    }

    public class TwilioMessageDto
    {
        public string Body { get; set; }  // Texto recibido
        public string From { get; set; }  // whatsapp:+1...
        public string To { get; set; }    // Tu número WhatsApp en Twilio
    }

    public class OpenAiResponse
    {
        public List<Choice> Choices { get; set; }
        public class Choice { public Message Message { get; set; } }
        public class Message { public string Role { get; set; } public string Content { get; set; } }
    }
}