using ComplianceCopilot.Agent.Rag;
using ComplianceCopilot.Shared.Configuration;
using ComplianceCopilot.Shared.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// See docs/architecture.html for why ContentRootPath is set explicitly here (Host.CreateApplicationBuilder's
// default - the working directory - silently fails to find appsettings.json under `dotnet run --project`).
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
builder.Services.AddSingleton<OllamaClients>();

builder.Services.AddSingleton(sp =>
{
    var ragOptions = sp.GetRequiredService<IOptions<RagOptions>>().Value;
    var path = Path.Combine(AppContext.BaseDirectory, ragOptions.IndexPath);
    return RagIndexStore.Load(path);
});

builder.Services.AddSingleton<RagRetriever>();
builder.Services.AddSingleton<RagAnswerGenerator>();

// Full agent orchestration (intent routing, MCP client, verification) is added across later
// commits. For now this host also supports a standalone RAG-only CLI test path:
//   dotnet run --project src/ComplianceCopilot.Agent -- rag "your question here"

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

if (args.Length >= 2 && args[0] == "rag")
{
    var query = string.Join(' ', args[1..]);
    var retriever = host.Services.GetRequiredService<RagRetriever>();
    var generator = host.Services.GetRequiredService<RagAnswerGenerator>();

    Console.WriteLine($"Query: {query}\n");

    var retrieved = await retriever.RetrieveAsync(query);
    Console.WriteLine($"Retrieved {retrieved.Count} chunk(s):");
    foreach (var r in retrieved)
        Console.WriteLine($"  [{r.Chunk.ChunkId}] similarity={r.Similarity:F3} - {r.Chunk.Heading}");

    Console.WriteLine();
    var answer = await generator.GenerateAsync(query, retrieved);
    Console.WriteLine("Answer:");
    Console.WriteLine(answer);
}
else
{
    var ollama = host.Services.GetRequiredService<IOptions<OllamaOptions>>().Value;
    var rag = host.Services.GetRequiredService<IOptions<RagOptions>>().Value;
    logger.LogInformation(
        "ComplianceCopilot.Agent foundation ready. ChatModel={ChatModel}, TopK={TopK}. Try: dotnet run --project src/ComplianceCopilot.Agent -- rag \"your question\"",
        ollama.ChatModel,
        rag.TopK);
}
