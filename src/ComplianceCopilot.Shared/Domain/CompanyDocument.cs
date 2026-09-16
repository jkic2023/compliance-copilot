namespace ComplianceCopilot.Shared.Domain;

/// <summary>
/// A mock document record — metadata + a text summary, not an actual file. There is no
/// real file storage being mocked here (see README assumptions).
/// </summary>
public sealed record CompanyDocument(
    string CompanyId,
    string DocumentType,
    DateOnly IssuedDate,
    string ContentSummary);
