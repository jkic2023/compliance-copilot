namespace ComplianceCopilot.Shared.Domain;

/// <summary>
/// Deterministically resolves the free-text company name the LLM extracted into a real
/// Company from the current user's actual list - this is tool-call argument construction
/// happening in code, not the LLM guessing at a companyId it was never given. Returns null on
/// no match or an ambiguous partial match rather than picking one - a wrong guess here would
/// mean calling a tool against the wrong company.
/// </summary>
public static class CompanyResolver
{
    public static Company? Resolve(string? companyName, IReadOnlyList<Company> companies)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return null;

        var exact = companies.FirstOrDefault(c =>
            string.Equals(c.Name, companyName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var partialMatches = companies
            .Where(c => c.Name.Contains(companyName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return partialMatches.Count == 1 ? partialMatches[0] : null;
    }
}
