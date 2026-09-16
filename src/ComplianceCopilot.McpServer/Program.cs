using ComplianceCopilot.Shared.Configuration;
using Microsoft.Extensions.Configuration;
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

builder.Services
    .AddOptions<McpServerOptions>()
    .Bind(builder.Configuration.GetSection(McpServerOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);

// MCP tool registration (get_user_companies, get_company_compliance_status, get_document) and
// the stdio transport are added in a later commit — this host is foundation only for now.

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
logger.LogInformation(
    "ComplianceCopilot.McpServer foundation ready. CurrentUserId={CurrentUserId}, DataFilePath={DataFilePath}",
    options.CurrentUserId,
    options.DataFilePath);
