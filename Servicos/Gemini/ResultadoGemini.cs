
namespace WhatsappGeminiDocker.Servicos.Gemini;

public record ResultadoGemini(StatusResultadoGemini Status, string Mensagem = "", string? Erro = null);

public enum StatusResultadoGemini
{
    Sucesso,
    Erro
}