using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace WhatsappGeminiDocker;

public static class Endpoints
{
    private const string GeminiSystemInstructions = "Como um bom amigo na faixa dos 30 anos de idade, responda as perguntas e faça comentários sobre afirmações de maneira informal e com frases curtas como respostas de um bate papo no whatsapp."; // Substitua pelas instruções do sistema

    internal static void MapGetChat(this WebApplication app)
    {
        app.MapGet("/Chat", (
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken,
            [FromQuery(Name = "hub.challenge")] string? challenge,
            IConfiguration config,
            ILogger<Program> logger) =>
        {
            logger.LogInformation("C# HTTP GET trigger function processed a request.");

            var whatsappTokens = config.GetSection("Whatsapp").Get<WhatsappTokens>();

            if (mode == "subscribe" && verifyToken == whatsappTokens?.VERIFY_TOKEN && challenge != null)
            {
                return Results.Ok(int.Parse(challenge));
            }
            else
            {
                return Results.StatusCode(403);
            }
        });
    }

    public static void MapPostChat(this WebApplication app)
    {
        app.MapPost("/Chat", async (
            HttpContext context,
            IConfiguration config,
            IHttpClientFactory httpClientFactory, // Inject IHttpClientFactory
            ILogger<Program> logger) =>
        {
            logger.LogInformation("C# HTTP POST trigger function processed a request.");

            var whatsappTokens = config.GetSection("Whatsapp").Get<WhatsappTokens>(); // Get tokens from config
            if (whatsappTokens == null || string.IsNullOrEmpty(whatsappTokens.ACCESS_TOKEN))
            {
                logger.LogError("Whatsapp tokens are not configured correctly.");
                return Results.StatusCode(500); // Internal Server Error if tokens are missing.
            }

            var gemini = config.GetSection("Gemini").Get<GeminiKey>();
            if (gemini == null || string.IsNullOrEmpty(gemini.API_KEY))
            {
                logger.LogError("Gemini key is not configured correctly.");
                return Results.StatusCode(500); // Internal Server Error if key is missing.
            }

            // Use System.Text.Json for deserialization.
            WhatsappWebhookData? body = await context.Request.ReadFromJsonAsync<WhatsappWebhookData>();

            if (body?.Entry == null)
            {
                return Results.Ok(); // Return 200 OK even if no messages to avoid retries
            }

            logger.LogInformation($"Body obtido.");

            foreach (var entry in body.Entry)
            {
                logger.LogInformation($"Entry: {JsonSerializer.Serialize(entry)}.");

                if (entry.Changes == null) continue;

                foreach (var change in entry.Changes)
                {
                    logger.LogInformation($"Change: {JsonSerializer.Serialize(change)}.");
                    var value = change.Value;
                    if (value == null) continue;

                    var phoneNumberId = value?.Metadata?.PhoneNumberId;
                    logger.LogInformation($"PhoneNumberId: {phoneNumberId}.");

                    if (value?.Messages == null) continue;

                    foreach (var message in value.Messages)
                    {
                        if (message?.Type != "text") continue;

                        var from = message.From;
                        var messageBody = message?.Text?.Body;

                        logger.LogInformation($"Phone Number: {phoneNumberId} Message: {from} | Body: {messageBody}.");
                        var replyMessage = await SendToGemini(messageBody, gemini, httpClientFactory, logger);

                        if (phoneNumberId == null || from == null || replyMessage == null) continue;

                        await SendReply(phoneNumberId, from, replyMessage, whatsappTokens.ACCESS_TOKEN, httpClientFactory,
                            logger);
                        return Results.Ok(); // Acknowledge each message individually.
                    }
                }
            }

            return Results.Ok(); // Return 200 OK even if no messages were processed.
        });
    }


    public static void MapPostTest(this WebApplication app)
    {
        app.MapPost("/Test", async (
            [FromBody] string message, // System.Text.Json can bind simple strings from the body.
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            ILogger<Program> logger) =>
        {
            logger.LogInformation("C# HTTP POST trigger function processed a request.");

            var gemini = config.GetSection("Gemini").Get<GeminiKey>();
            if (gemini == null || string.IsNullOrEmpty(gemini.API_KEY))
            {
                logger.LogError("Gemini key is not configured correctly.");
                return Results.StatusCode(500);
            }

            var response = await SendToGemini(message, gemini, httpClientFactory, logger);
            return Results.Ok(response); // Return the response string directly.
        });
    }


    private static async Task<string> SendToGemini(string? message, GeminiKey gemini, IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        // Use anonymous types for the JSON structure.  System.Text.Json handles this well.
        var json = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = GeminiSystemInstructions } }
            },
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = message ?? string.Empty } } // Handle null message
                }
            }
        };

        // Use System.Text.Json for serialization.
        var serializedData = JsonSerializer.Serialize(json);
        var data = new StringContent(serializedData, Encoding.UTF8, "application/json");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={gemini.API_KEY}";

        var client = httpClientFactory.CreateClient();

        try
        {
            logger.LogInformation($"Url: {url}.");
            logger.LogInformation($"Data: {serializedData}.");

            var response = await client.PostAsync(url, data);
            if (response.IsSuccessStatusCode)
            {
                // Use System.Text.Json to deserialize the response.
                var responseContent = await response.Content.ReadFromJsonAsync<GeminiResponse>();

                if (responseContent == null || responseContent.Candidates == null || responseContent.Candidates.Count == 0)
                {
                    logger.LogWarning("Gemini returned an empty or invalid response.");
                    return string.Empty;
                }

                logger.LogInformation($"Response from Gemini: {JsonSerializer.Serialize(responseContent)}");

                var textContent = responseContent?.Candidates?[0]?.Content?.Parts?[0]?.Text;
                return textContent ?? string.Empty;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError($"Error sending to Gemini: {response.StatusCode} - {errorContent}");
                return string.Empty;
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Exception sending to Gemini: {ex.Message}");
            return string.Empty;
        }
    }


    private static async Task SendReply(string phoneNumberId, string to, string replyMessage, string whatsappToken,
        IHttpClientFactory httpClientFactory, ILogger logger)
    {
        var json = new
        {
            messaging_product = "whatsapp",
            to = to,
            text = new { body = replyMessage }
        };

        // Use System.Text.Json for serialization.
        var serializedData = JsonSerializer.Serialize(json);
        var data = new StringContent(serializedData, Encoding.UTF8, "application/json");
        var path = $"/v22.0/{phoneNumberId}/messages?access_token={whatsappToken}";
        var url = $"https://graph.facebook.com{path}";

        var client = httpClientFactory.CreateClient();

        try
        {
            logger.LogInformation($"Url: {url}.");
            logger.LogInformation($"Data: {serializedData}.");

            var response = await client.PostAsync(url, data);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError($"Error sending reply: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Exception sending reply: {ex.Message}");
        }
    }

    // DTOs para System.Text.Json
    public class WhatsappWebhookData
    {
        [JsonPropertyName("entry")]
        public List<Entry>? Entry { get; set; }
        [JsonPropertyName("object")]
        public string? Object { get; set; }
    }

    public class Entry
    {
        [JsonPropertyName("changes")]
        public List<Change>? Changes { get; set; }
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("time")]
        public long Time { get; set; }
    }

    public class Change
    {
        [JsonPropertyName("field")]
        public string? Field { get; set; }
        [JsonPropertyName("value")]
        public Value? Value { get; set; }
    }

    public class Value
    {
        [JsonPropertyName("contacts")]
        public List<Contact>? Contacts { get; set; }
        [JsonPropertyName("messages")]
        public List<Message>? Messages { get; set; }
        [JsonPropertyName("messaging_product")]
        public string? MessagingProduct { get; set; }
        [JsonPropertyName("metadata")]
        public Metadata? Metadata { get; set; }
        [JsonPropertyName("statuses")]
        public List<Status>? Statuses { get; set; }
    }
    public class Metadata
    {
        [JsonPropertyName("display_phone_number")]
        public string? DisplayPhoneNumber { get; set; }
        [JsonPropertyName("phone_number_id")]
        public string? PhoneNumberId { get; set; }
    }

    public class Message
    {
        [JsonPropertyName("from")]
        public string? From { get; set; }
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("text")]
        public Text? Text { get; set; }
        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }
        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    public class Text
    {
        [JsonPropertyName("body")]
        public string? Body { get; set; }
    }

    public class Contact
    {
        [JsonPropertyName("profile")]
        public Profile? Profile { get; set; }
        [JsonPropertyName("wa_id")]
        public string? WaId { get; set; }
    }
    public class Profile
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
    public class Status
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("status")]
        public string? MessageStatus { get; set; }
        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }
        [JsonPropertyName("recipient_id")]
        public string? RecipientId { get; set; }
    }

    public class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate>? Candidates { get; set; }

        [JsonPropertyName("promptFeedback")]
        public PromptFeedback? PromptFeedback { get; set; }
    }

    public class Candidate
    {
        [JsonPropertyName("content")]
        public Content? Content { get; set; }

        [JsonPropertyName("finishReason")]
        public string? FinishReason { get; set; }

        [JsonPropertyName("index")]
        public int? Index { get; set; }

        [JsonPropertyName("safetyRatings")]
        public List<SafetyRating>? SafetyRatings { get; set; }
    }

    public class Content
    {
        [JsonPropertyName("parts")]
        public List<Part>? Parts { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }
    }

    public class Part
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public class SafetyRating
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("probability")]
        public string? Probability { get; set; }
    }

    public class PromptFeedback
    {
        [JsonPropertyName("blockReason")]
        public string? BlockReason { get; set; }
        [JsonPropertyName("safetyRatings")]
        public List<SafetyRating>? SafetyRatings { get; set; }
    }

    class GeminiKey
    {
        public string? API_KEY { get; set; }
    }
}