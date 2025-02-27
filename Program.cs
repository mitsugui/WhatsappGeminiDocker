using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using WhatsappGeminiDocker;

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

var app = builder.Build();

app.MapGetChat();

app.MapPostChat();

app.MapPostTest();

app.Run();
