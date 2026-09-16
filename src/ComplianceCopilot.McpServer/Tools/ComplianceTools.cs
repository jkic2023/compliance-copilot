using System.ComponentModel;
using ComplianceCopilot.Shared.Configuration;
using ComplianceCopilot.Shared.Domain;
using ComplianceCopilot.Shared.Results;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ComplianceCopilot.McpServer.Tools;

/// <summary>
/// The three MCP tools over the mocked user/entity data. Every tool is scoped to
/// ComplianceMcpOptions.CurrentUserId - a company that exists but belongs to a different user
/// returns Forbidden, not NotFound and not a silent empty result. Extra parameters
/// (MockDataSet, IOptions&lt;ComplianceMcpOptions&gt;, TimeProvider) are resolved via DI, not
/// supplied by the model - only [Description]-annotated parameters are part of the tool's
/// JSON schema.
/// </summary>
[McpServerToolType]
public static class ComplianceTools
{
    [McpServerTool(Name = "get_user_companies")]
    [Description("List all companies belonging to the current user.")]
    public static Task<ToolOutcome<IReadOnlyList<Company>>> GetUserCompanies(
        MockDataSet data,
        IOptions<ComplianceMcpOptions> options)
    {
        var currentUserId = options.Value.CurrentUserId;
        var companies = data.Companies
            .Where(c => c.OwnerUserId == currentUserId)
            .ToList();

        return Task.FromResult(ToolOutcome<IReadOnlyList<Company>>.Success(companies));
    }

    [McpServerTool(Name = "get_company_compliance_status")]
    [Description("Get the upcoming and overdue compliance obligations for a company owned by the current user. Call get_user_companies first to obtain a valid companyId.")]
    public static Task<ToolOutcome<ComplianceStatusView>> GetCompanyComplianceStatus(
        [Description("The companyId of the company to check, e.g. 'acme-1'.")] string companyId,
        MockDataSet data,
        IOptions<ComplianceMcpOptions> options,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(companyId))
        {
            return Task.FromResult(ToolOutcome<ComplianceStatusView>.Failure(
                ToolErrorKind.ValidationFailed, "companyId is required."));
        }

        var company = data.Companies.FirstOrDefault(c => c.CompanyId == companyId);
        if (company is null)
        {
            return Task.FromResult(ToolOutcome<ComplianceStatusView>.Failure(
                ToolErrorKind.NotFound, $"No company found with companyId '{companyId}'."));
        }

        if (company.OwnerUserId != options.Value.CurrentUserId)
        {
            return Task.FromResult(ToolOutcome<ComplianceStatusView>.Failure(
                ToolErrorKind.Forbidden, $"Company '{companyId}' does not belong to the current user."));
        }

        var status = data.ComplianceStatuses.FirstOrDefault(s => s.CompanyId == companyId);
        if (status is null)
        {
            return Task.FromResult(ToolOutcome<ComplianceStatusView>.Failure(
                ToolErrorKind.NotFound, $"No compliance status recorded for companyId '{companyId}'."));
        }

        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var view = ComplianceStatusEvaluator.Evaluate(status, asOf);
        return Task.FromResult(ToolOutcome<ComplianceStatusView>.Success(view));
    }

    [McpServerTool(Name = "get_document")]
    [Description("Fetch a mock document record (e.g. CertificateOfRegistration, Constitution, AsicExtract) for a company owned by the current user.")]
    public static Task<ToolOutcome<CompanyDocument>> GetDocument(
        [Description("The companyId of the company, e.g. 'acme-1'.")] string companyId,
        [Description("The document type, e.g. 'CertificateOfRegistration', 'Constitution', or 'AsicExtract'.")] string documentType,
        MockDataSet data,
        IOptions<ComplianceMcpOptions> options)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(documentType))
        {
            return Task.FromResult(ToolOutcome<CompanyDocument>.Failure(
                ToolErrorKind.ValidationFailed, "companyId and documentType are both required."));
        }

        var company = data.Companies.FirstOrDefault(c => c.CompanyId == companyId);
        if (company is null)
        {
            return Task.FromResult(ToolOutcome<CompanyDocument>.Failure(
                ToolErrorKind.NotFound, $"No company found with companyId '{companyId}'."));
        }

        if (company.OwnerUserId != options.Value.CurrentUserId)
        {
            return Task.FromResult(ToolOutcome<CompanyDocument>.Failure(
                ToolErrorKind.Forbidden, $"Company '{companyId}' does not belong to the current user."));
        }

        var document = data.Documents.FirstOrDefault(d =>
            d.CompanyId == companyId &&
            string.Equals(d.DocumentType, documentType, StringComparison.OrdinalIgnoreCase));

        if (document is null)
        {
            return Task.FromResult(ToolOutcome<CompanyDocument>.Failure(
                ToolErrorKind.NotFound, $"No document of type '{documentType}' found for companyId '{companyId}'."));
        }

        return Task.FromResult(ToolOutcome<CompanyDocument>.Success(document));
    }
}
