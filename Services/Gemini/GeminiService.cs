
using System.Text;
using System.Text.Json;

namespace WhatsappGeminiDocker.Services.Gemini;

internal class GeminiService
{
    private readonly GeminiKey? _geminiKey;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;


    private const string GeminiSystemInstructions = "Como um bom amigo na faixa dos 30 anos de idade, responda as perguntas e faça comentários sobre afirmações de maneira informal e com frases curtas como respostas de um bate papo no whatsapp."; // Substitua pelas instruções do sistema


    public GeminiService(IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<Program> logger)
    {
        _geminiKey = config.GetSection("Gemini").Get<GeminiKey>();
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<(IResult, string)> SendToGemini(string? message)
    {
        if (_geminiKey == null || string.IsNullOrEmpty(_geminiKey.API_KEY))
        {
            _logger.LogError("Gemini key is not configured correctly.");
            return (Results.StatusCode(500), string.Empty); // Internal Server Error if key is missing.
        }

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
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={_geminiKey.API_KEY}";

        var client = _httpClientFactory.CreateClient();

        try
        {
            _logger.LogInformation($"Url: {url}.");
            _logger.LogInformation($"Data: {serializedData}.");

            var response = await client.PostAsync(url, data);
            if (response.IsSuccessStatusCode)
            {
                // Use System.Text.Json to deserialize the response.
                var responseContent = await response.Content.ReadFromJsonAsync<GeminiResponse>();

                if (responseContent == null || responseContent.Candidates == null || responseContent.Candidates.Count == 0)
                {
                    _logger.LogWarning("Gemini returned an empty or invalid response.");
                    return (Results.Ok(), string.Empty);
                }

                _logger.LogInformation($"Response from Gemini: {JsonSerializer.Serialize(responseContent)}");

                var textContent = responseContent?.Candidates?[0]?.Content?.Parts?[0]?.Text;
                return (Results.Ok(), textContent ?? string.Empty);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error sending to Gemini: {response.StatusCode} - {errorContent}");
                return (Results.Ok(), string.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception sending to Gemini: {ex.Message}");
            return (Results.StatusCode(500), string.Empty);
        }
    }
}
