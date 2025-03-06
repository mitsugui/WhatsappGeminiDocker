using Microsoft.AspNetCore.Mvc;
using WhatsappGeminiDocker.Servicos.Gemini;
using WhatsappGeminiDocker.Servicos.Whatsapp;

namespace WhatsappGeminiDocker;

public static class Endpoints
{
    internal static void MapGetChat(this WebApplication app)
    {
        app.MapGet("/Chat", (
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken,
            [FromQuery(Name = "hub.challenge")] string? challenge,
            ILogger<Program> logger,
            ServicoWhatsapp servicoWhatsapp) =>
        {
            logger.LogInformation("HTTP GET Chat.");

            return servicoWhatsapp.Subscribe(mode, verifyToken, challenge);
        });
    }

    public static void MapPostChat(this WebApplication app)
    {
        app.MapPost("/Chat", async (
            HttpContext context,
            ILogger<Program> logger,
            ServicoWhatsapp servicoWhatsapp,
            ServicoGemini geminiService) =>
        {
            logger.LogInformation("C# HTTP POST trigger function processed a request.");

            var resultadosWhatsapp = await servicoWhatsapp.ProcessTextMessages(context);
            foreach (var resultadoWhatsapp in resultadosWhatsapp)
            {
                var resultado = await geminiService.EnviarMensagem(resultadoWhatsapp.Mensagem);
                if (resultado.Status != StatusResultadoGemini.Sucesso)
                {
                    return Results.StatusCode(500);
                }

                var replyMessage = resultado.Mensagem;

                await servicoWhatsapp.SendReply(resultadoWhatsapp.IdNumeroTelefone, resultadoWhatsapp.De, replyMessage);
                return Results.Ok(); // Acknowledge each message individually.
            }

            return Results.Ok(); // Return 200 OK even if no messages were processed.
        });
    }

    public static void MapPostTest(this WebApplication app)
    {
        app.MapPost("/Test", async (
            [FromBody] string message, // System.Text.Json can bind simple strings from the body.
            ILogger<Program> logger,
            ServicoGemini geminiService) =>
        {
            logger.LogInformation("C# HTTP POST trigger function processed a request.");

            var resultado = await geminiService.EnviarMensagem(message);
            return resultado.Status == StatusResultadoGemini.Sucesso
                ? Results.Ok(resultado.Mensagem)
                : Results.StatusCode(500);
        });
    }
}