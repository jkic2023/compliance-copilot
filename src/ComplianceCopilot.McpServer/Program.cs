using ComplianceCopilot.Shared.Configuration;
using ComplianceCopilot.Shared.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// See ComplianceCopilot.Agent/Program.cs for why ContentRootPath is set explicitly here.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Critical for a stdio MCP server: stdout is the JSON-RPC protocol channel. Any log line
// written to stdout corrupts every message after it. Route all logging to stderr instead.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddOptions<ComplianceMcpOptions>()
    .Bind(builder.Configuration.GetSection(ComplianceMcpOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<ComplianceMcpOptions>>().Value;
    var path = Path.Combine(AppContext.BaseDirectory, options.DataFilePath);
    return MockDataLoader.LoadFromFile(path);
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
