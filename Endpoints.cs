using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using WhatsappGeminiDocker.Services.Gemini;
using WhatsappGeminiDocker.Services.Whatsapp;

namespace WhatsappGeminiDocker;

public static class Endpoints
{
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
            ILogger<Program> logger,
            GeminiService geminiService) =>
        {
            logger.LogInformation("C# HTTP POST trigger function processed a request.");

            var whatsappTokens = config.GetSection("Whatsapp").Get<WhatsappTokens>(); // Get tokens from config
            if (whatsappTokens == null || string.IsNullOrEmpty(whatsappTokens.ACCESS_TOKEN))
            {
                logger.LogError("Whatsapp tokens are not configured correctly.");
                return Results.StatusCode(500); // Internal Server Error if tokens are missing.
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
                        var (result, replyMessage) = await geminiService.SendToGemini(messageBody);
                        if (result != Results.Ok()) return result;

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
            ILogger<Program> logger,
            GeminiService geminiService) =>
        {
            logger.LogInformation("C# HTTP POST trigger function processed a request.");

            var (result, text) = await geminiService.SendToGemini(message);
            return result == Results.Ok() ? Results.Ok(text) : result;
        });
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
}