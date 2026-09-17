using ComplianceCopilot.Agent.Mcp;
using ComplianceCopilot.Agent.Orchestration;
using ComplianceCopilot.Agent.Rag;
using ComplianceCopilot.Shared.Configuration;
using ComplianceCopilot.Shared.Agent;
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

builder.Services
    .AddOptions<ComplianceMcpClientOptions>()
    .Bind(builder.Configuration.GetSection(ComplianceMcpClientOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OllamaClients>();
builder.Services.AddSingleton<McpToolClient>();
builder.Services.AddSingleton<AgentOrchestrator>();

builder.Services.AddSingleton(sp =>
{
    var ragOptions = sp.GetRequiredService<IOptions<RagOptions>>().Value;
    var path = Path.Combine(AppContext.BaseDirectory, ragOptions.IndexPath);
    return RagIndexStore.Load(path);
});

builder.Services.AddSingleton<RagRetriever>();
builder.Services.AddSingleton<RagAnswerGenerator>();
builder.Services.AddSingleton<VerifiedRagAnswerGenerator>();
builder.Services.AddSingleton<IntentExtractor>();

//   dotnet run --project src/ComplianceCopilot.Agent -- ask "your question here"     (full pipeline)
//   dotnet run --project src/ComplianceCopilot.Agent -- rag "your question here"     (RAG only + grounding check, standalone)
//   dotnet run --project src/ComplianceCopilot.Agent -- intent "your question here"  (routing only, standalone)

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

if (args.Length >= 2 && args[0] == "ask")
{
    var query = string.Join(' ', args[1..]);
    var orchestrator = host.Services.GetRequiredService<AgentOrchestrator>();

    Console.WriteLine($"Query: {query}\n");
    var answer = await orchestrator.HandleAsync(query);
    Console.WriteLine("Answer:");
    Console.WriteLine(answer);
}
else if (args.Length >= 2 && args[0] == "rag")
{
    var query = string.Join(' ', args[1..]);
    var retriever = host.Services.GetRequiredService<RagRetriever>();
    var verifiedGenerator = host.Services.GetRequiredService<VerifiedRagAnswerGenerator>();

    Console.WriteLine($"Query: {query}\n");

    var retrieved = await retriever.RetrieveAsync(query);
    Console.WriteLine($"Retrieved {retrieved.Count} chunk(s):");
    foreach (var r in retrieved)
        Console.WriteLine($"  [{r.Chunk.ChunkId}] similarity={r.Similarity:F3} - {r.Chunk.Heading}");

    Console.WriteLine();
    var result = await verifiedGenerator.AnswerAsync(query);
    Console.WriteLine("Answer:");
    Console.WriteLine(result.Answer);
    Console.WriteLine();
    Console.WriteLine($"Grounded: {result.IsGrounded} (regeneration attempted: {result.RegenerationAttempted})");
}
else if (args.Length >= 2 && args[0] == "intent")
{
    var query = string.Join(' ', args[1..]);
    var extractor = host.Services.GetRequiredService<IntentExtractor>();

    Console.WriteLine($"Query: {query}\n");

    var intent = await extractor.ExtractAsync(query);
    Console.WriteLine($"Intent: NeedsRag={intent.NeedsRag}, ToolIntent={intent.ToolIntent}, CompanyName={intent.CompanyName ?? "<none>"}, DocumentType={intent.DocumentType ?? "<none>"}");
    Console.WriteLine($"Route:  {QueryRouter.Decide(intent)}");
}
else
{
    var ollama = host.Services.GetRequiredService<IOptions<OllamaOptions>>().Value;
    var rag = host.Services.GetRequiredService<IOptions<RagOptions>>().Value;
    logger.LogInformation(
        "ComplianceCopilot.Agent foundation ready. ChatModel={ChatModel}, TopK={TopK}. Try: dotnet run --project src/ComplianceCopilot.Agent -- ask \"your question\"",
        ollama.ChatModel,
        rag.TopK);
}

// Explicit disposal, not left to process exit: McpToolClient owns a child OS process (the
// McpServer), and that process should be told to shut down cleanly rather than orphaned.
var mcpToolClient = host.Services.GetRequiredService<McpToolClient>();
await mcpToolClient.DisposeAsync();
