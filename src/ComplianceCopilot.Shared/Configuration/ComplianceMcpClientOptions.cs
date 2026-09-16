namespace ComplianceCopilot.Shared.Configuration;

/// <summary>
/// Config section "McpClient" - where the Agent finds the McpServer executable to launch as
/// a subprocess. Null ServerExecutablePath means "auto-detect relative to this Agent's own
/// build output" (see McpToolClient) - override here if that guess is wrong for your machine.
/// Named ComplianceMcpClientOptions (not McpClientOptions) to avoid colliding with the SDK's
/// own ModelContextProtocol.Client.McpClientOptions type - same reason ComplianceMcpOptions
/// was renamed on the server side in commit 3.
/// </summary>
public sealed class ComplianceMcpClientOptions
{
    public const string SectionName = "McpClient";

    public string? ServerExecutablePath { get; set; }
    public int ShutdownTimeoutSeconds { get; set; } = 10;
}
