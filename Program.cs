using WhatsappGeminiDocker;
using WhatsappGeminiDocker.Servicos.Gemini;
using WhatsappGeminiDocker.Servicos.Whatsapp;

var builder = WebApplication.CreateBuilder(args);

// Add configuration for appsettings.json and environment variables.  Crucially, bind to a section.
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddLogging();
builder.Services.AddHttpClient(); // Add HttpClient

// Configure strongly typed settings objects.
builder.Services.Configure<WhatsappTokens>(
    builder.Configuration.GetSection("Whatsapp"));

builder.Services.AddScoped<ServicoWhatsapp>();
builder.Services.AddScoped<ServicoGemini>();

var app = builder.Build();

app.MapGetChat();

app.MapPostChat();

app.MapPostTest();

app.Run();
