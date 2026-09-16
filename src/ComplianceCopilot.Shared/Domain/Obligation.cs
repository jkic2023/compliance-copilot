namespace ComplianceCopilot.Shared.Domain;

/// <summary>A single upcoming or overdue statutory obligation for a company.</summary>
public sealed record Obligation(string Description, DateOnly DueDate);
