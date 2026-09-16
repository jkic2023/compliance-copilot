using ComplianceCopilot.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ContentRootPath is explicit rather than left to the default (current working directory) -
// Host.CreateApplicationBuilder otherwise silently fails to find appsettings.json whenever this
// process is launched from anywhere other than its own output folder (e.g. `dotnet run
// --project`), because AddJsonFile treats a missing file as optional with no warning.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services
    .AddOptions<OllamaOptions>()
    .Bind(builder.Configuration.GetSection(OllamaOptions.SectionName));

builder.Services
    .AddOptions<RagOptions>()
    .Bind(builder.Configuration.GetSection(RagOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);

// OllamaApiClient registration, the MCP client, RAG retrieval, orchestration, and verification
// are added across later commits — this host is foundation (config/logging/DI) only for now.

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var ollama = host.Services.GetRequiredService<IOptions<OllamaOptions>>().Value;
var rag = host.Services.GetRequiredService<IOptions<RagOptions>>().Value;
logger.LogInformation(
    "ComplianceCopilot.Agent foundation ready. ChatModel={ChatModel}, TopK={TopK}",
    ollama.ChatModel,
    rag.TopK);
