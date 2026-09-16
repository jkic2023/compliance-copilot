using System.Text.Json;
using ComplianceCopilot.Shared.Configuration;
using ComplianceCopilot.Shared.Results;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ComplianceCopilot.Agent.Mcp;

/// <summary>
/// Wraps the connection to ComplianceCopilot.McpServer, launched as a genuinely separate OS
/// process over stdio (not an in-process call) - connects lazily on first use and is reused
/// for the process's lifetime, then disposed explicitly on shutdown (see Program.cs) so the
/// child process doesn't get left running.
/// </summary>
public sealed class McpToolClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _serverExecutablePath;
    private readonly int _shutdownTimeoutSeconds;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private McpClient? _client;

    public McpToolClient(IOptions<ComplianceMcpClientOptions> options)
    {
        var opts = options.Value;
        _serverExecutablePath = opts.ServerExecutablePath ?? ResolveDefaultServerPath();
        _shutdownTimeoutSeconds = opts.ShutdownTimeoutSeconds;
    }

    public async Task<ToolOutcome<T>> CallToolAsync<T>(
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        try
        {
            var client = await GetClientAsync(ct);
            var result = await client.CallToolAsync(toolName, arguments, cancellationToken: ct);
            var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;

            if (string.IsNullOrEmpty(text))
                return ToolOutcome<T>.Failure(ToolErrorKind.Unavailable, $"'{toolName}' returned no content.");

            return JsonSerializer.Deserialize<ToolOutcome<T>>(text, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            // The MCP server process is unreachable/unstartable - degrade, don't crash and
            // don't fabricate an answer. This is the one place a real exception is deliberately
            // caught and converted into the same typed outcome as an ordinary tool failure.
            return ToolOutcome<T>.Failure(
                ToolErrorKind.Unavailable,
                $"Could not reach the company records service: {ex.Message}");
        }
    }

    private async Task<McpClient> GetClientAsync(CancellationToken ct)
    {
        if (_client is not null)
            return _client;

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_client is not null)
                return _client;

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "ComplianceCopilot.McpServer",
                Command = _serverExecutablePath,
                ShutdownTimeout = TimeSpan.FromSeconds(_shutdownTimeoutSeconds),
            });

            _client = await McpClient.CreateAsync(transport, cancellationToken: ct);
            return _client;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Agent's own output dir looks like .../src/ComplianceCopilot.Agent/bin/{Config}/{TFM}/ -
    /// mirror the same Config/TFM into the McpServer project rather than hardcoding "Debug",
    /// so this doesn't quietly break on a Release build.
    /// </summary>
    private static string ResolveDefaultServerPath()
    {
        var agentOutputDir = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tfm = Path.GetFileName(agentOutputDir);
        var config = Path.GetFileName(Path.GetDirectoryName(agentOutputDir)!);
        var srcDir = Path.GetFullPath(Path.Combine(agentOutputDir, "..", "..", "..", ".."));

        return Path.Combine(srcDir, "ComplianceCopilot.McpServer", "bin", config, tfm, "ComplianceCopilot.McpServer.exe");
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }
}
