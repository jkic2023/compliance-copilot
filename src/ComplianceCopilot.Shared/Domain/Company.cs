namespace ComplianceCopilot.Shared.Domain;

/// <summary>
/// A mock company entity. <see cref="OwnerUserId"/> is what the MCP server's per-tenant
/// scoping constraint checks a caller's request against.
/// </summary>
public sealed record Company(
    string CompanyId,
    string OwnerUserId,
    string Name,
    string Abn,
    DateOnly IncorporationDate);
