using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhatsappGeminiDocker.Servicos.Whatsapp;

internal class ServicoWhatsapp
{
    private readonly WhatsappTokens? _whatsappTokens;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public ServicoWhatsapp(IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<Program> logger)
    {
        _whatsappTokens = config.GetSection("Whatsapp").Get<WhatsappTokens>();
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IResult Subscribe(string? mode, string? verifyToken, string? challenge)
    {
        if (_whatsappTokens == null || string.IsNullOrEmpty(_whatsappTokens.ACCESS_TOKEN))
        {
            _logger.LogError("Whatsapp tokens are not configured correctly.");
            return Results.StatusCode(500); // Internal Server Error if tokens are missing.
        }

        if (mode == "subscribe" && verifyToken == _whatsappTokens?.VERIFY_TOKEN && challenge != null)
        {
            return Results.Ok(int.Parse(challenge));
        }
        else
        {
            return Results.StatusCode(403);
        }
    }

    public async Task<IEnumerable<ResultadoWhatsapp>> ProcessTextMessages(HttpContext context)
    {
        // Use System.Text.Json for deserialization.
        var body = await context.Request.ReadFromJsonAsync<WhatsappWebhookData>();

        return Iterar();

        IEnumerable<ResultadoWhatsapp> Iterar()
        {
            if (body?.Entry == null)
            {
                yield break; // Return 200 OK even if no messages to avoid retries
            }

            _logger.LogInformation($"Body obtido.");

            foreach (var entry in body.Entry)
            {
                _logger.LogInformation($"Entry: {JsonSerializer.Serialize(entry)}.");

                if (entry.Changes == null) continue;

                foreach (var change in entry.Changes)
                {
                    _logger.LogInformation($"Change: {JsonSerializer.Serialize(change)}.");
                    var value = change.Value;
                    if (value == null) continue;

                    var phoneNumberId = value?.Metadata?.PhoneNumberId;
                    _logger.LogInformation($"PhoneNumberId: {phoneNumberId}.");

                    if (value?.Messages == null) continue;

                    foreach (var message in value.Messages)
                    {
                        if (message?.Type != "text") continue;

                        var from = message.From;
                        var messageBody = message?.Text?.Body;

                        _logger.LogInformation($"Phone Number: {phoneNumberId} Message: {from} | Body: {messageBody}.");

                        if (messageBody == null || phoneNumberId == null || from == null) continue;

                        yield return new ResultadoWhatsapp(messageBody, phoneNumberId, from); // Acknowledge each message individually.
                    }
                }
            }
        }
    }

    public async Task SendReply(string phoneNumberId, string to, string replyMessage)
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
        var path = $"/v22.0/{phoneNumberId}/messages?access_token={_whatsappTokens?.ACCESS_TOKEN}";
        var url = $"https://graph.facebook.com{path}";

        var client = _httpClientFactory.CreateClient();

        try
        {
            _logger.LogInformation($"Url: {url}.");
            _logger.LogInformation($"Data: {serializedData}.");

            var response = await client.PostAsync(url, data);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error sending reply: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception sending reply: {ex.Message}");
        }
    }
}

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