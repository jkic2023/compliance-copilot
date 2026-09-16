namespace ComplianceCopilot.Shared.Configuration;

/// <summary>
/// Config section "Mcp" - which mock user this server instance is scoped to, and where its
/// data lives. Hardcoded-per-run "current user" stands in for real auth (see README assumptions).
/// Named ComplianceMcpOptions (not McpServerOptions) to avoid colliding with the SDK's own
/// ModelContextProtocol.Server.McpServerOptions type.
/// </summary>
public sealed class ComplianceMcpOptions
{
    public const string SectionName = "Mcp";

    public string CurrentUserId { get; set; } = "u1";
    public string DataFilePath { get; set; } = "Data/mock-data.json";
}
