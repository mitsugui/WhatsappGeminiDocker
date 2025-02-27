using System.Text;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

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

            using var reader = new StreamReader(context.Request.Body);
            var requestBody = await reader.ReadToEndAsync();
            dynamic? body = JsonConvert.DeserializeObject(requestBody);

            if (body?.entry == null)
            {
                return Results.Ok();  // Return 200 OK even if no messages to avoid retries
            }
            logger.LogInformation($"Body obtido.");

            foreach (var entry in body.entry)
            {
                logger.LogInformation($"Entry: {entry}.");
                foreach (var change in entry.changes)
                {
                    logger.LogInformation($"Change: {change}.");
                    var value = change.value;
                    if (value == null) continue;

                    var phoneNumberId = value?.metadata?.phone_number_id.ToString();
                    logger.LogInformation($"PhoneNumberId: {phoneNumberId}.");

                    if (value?.messages == null) continue;

                    foreach (var message in value.messages)
                    {
                        if (message?.type != "text") continue;

                        var from = message.from.ToString();
                        var messageBody = message?.text?.body;

                        logger.LogInformation($"Phone Number: {phoneNumberId} Message: {from} | Body: {messageBody}.");
                        var replyMessage = await SendToGemini(messageBody?.ToString(), gemini, httpClientFactory, logger);

                        await SendReply(phoneNumberId, from, replyMessage, whatsappTokens.ACCESS_TOKEN, httpClientFactory, logger);
                        return Results.Ok(); // Acknowledge each message individually.  Critical for avoiding infinite loops.
                    }
                }
            }

            return Results.Ok(); // Return 200 OK even if no messages were processed.
        });
    }

    public static void MapPostTest(this WebApplication app)
    {
        app.MapPost("/Test", async (
            [FromBody] string message,
            IConfiguration config,
            IHttpClientFactory httpClientFactory, // Inject IHttpClientFactory
            ILogger<Program> logger) =>
        {
            logger.LogInformation("C# HTTP POST trigger function processed a request.");

            var gemini = config.GetSection("Gemini").Get<GeminiKey>(); // Get Gemini key from config
            if (gemini == null || string.IsNullOrEmpty(gemini.API_KEY))
            {
                logger.LogError("Gemini key is not configured correctly.");
                return Results.StatusCode(500); // Internal Server Error if key is missing.
            }

            var response = await SendToGemini(message, gemini, httpClientFactory, logger);
            return Results.Ok(response);
        });
    }

    private static async Task<string> SendToGemini(string message, GeminiKey gemini, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        var json = new
        {
            system_instruction = new
            {
                parts = new { text = GeminiSystemInstructions }
            },
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = message } }
                }
            }
        };

        var serializedData = JsonConvert.SerializeObject(json);
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
                var responseContent = await response.Content.ReadAsStringAsync();
                if (responseContent == null) return string.Empty;

                logger.LogInformation($"Response from Gemini: {responseContent}");

                dynamic? jsonResponse = JsonConvert.DeserializeObject(responseContent);
                var textContent = jsonResponse?.candidates[0]?.content?.parts[0]?.text;

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

        var serializedData = JsonConvert.SerializeObject(json);
        var data = new StringContent(serializedData, Encoding.UTF8, "application/json");
        var path = $"/v22.0/{phoneNumberId}/messages?access_token={whatsappToken}";  //Use passed in token
        var url = $"https://graph.facebook.com{path}";

        // Use the named HttpClient from the factory.  Much cleaner than static.
        var client = httpClientFactory.CreateClient();

        try
        {
            logger.LogInformation($"Url: {url}.");
            logger.LogInformation($"Data: {serializedData}.");

            var response = await client.PostAsync(url, data);
            if (!response.IsSuccessStatusCode)
            {
                // Handle error logging or retry logic here. VERY IMPORTANT to log the error.
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError($"Error sending reply: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            // Handle exceptions (log, retry, etc.)  Essential to log the exception.
            logger.LogError($"Exception sending reply: {ex.Message}");
        }
    }
}

class GeminiKey
{
    public string? API_KEY { get; set; }
}