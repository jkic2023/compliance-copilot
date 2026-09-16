namespace ComplianceCopilot.Shared.Domain;

/// <summary>
/// A mock platform user. The MCP server is scoped to a single "current user" per run —
/// there is no login/auth flow, per the assessment's mocked-data scope.
/// </summary>
public sealed record User(string UserId, string Name, string Firm);
