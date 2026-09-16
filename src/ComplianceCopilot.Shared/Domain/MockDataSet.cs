namespace ComplianceCopilot.Shared.Domain;

/// <summary>
/// The full mock dataset as deserialized from the fixture JSON. Purely a data container —
/// no lookup/scoping logic here, that belongs to the MCP server itself.
/// </summary>
public sealed record MockDataSet(
    IReadOnlyList<User> Users,
    IReadOnlyList<Company> Companies,
    IReadOnlyList<ComplianceStatus> ComplianceStatuses,
    IReadOnlyList<CompanyDocument> Documents);
