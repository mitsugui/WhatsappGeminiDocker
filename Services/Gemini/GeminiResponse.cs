
using System.Text.Json.Serialization;

namespace WhatsappGeminiDocker.Services.Gemini;

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

public class PromptFeedback
{
    [JsonPropertyName("blockReason")]
    public string? BlockReason { get; set; }
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

public class SafetyRating
{
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("probability")]
    public string? Probability { get; set; }
}

public class Part
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
