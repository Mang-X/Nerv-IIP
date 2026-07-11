using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Inventory.Web.Application.Approval;

public sealed class StockCountAdjustmentApprovalOptions
{
    public const string SectionName = "Inventory:StockCountAdjustmentApproval";

    public decimal QuantityThreshold { get; init; } = 10m;
    public decimal AmountThreshold { get; init; } = 1000m;
    public string TemplateCode { get; init; } = "COUNT-VARIANCE";

    public bool RequiresApproval(decimal varianceQuantity, decimal varianceAmount) =>
        Math.Abs(varianceQuantity) > QuantityThreshold || Math.Abs(varianceAmount) > AmountThreshold;
}

public sealed record StockCountApprovalRequest(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string StartedBy,
    decimal Amount = 0m);

public sealed record StockCountApprovalResult(string ChainId);

public interface IStockCountApprovalClient
{
    Task<StockCountApprovalResult> StartApprovalAsync(StockCountApprovalRequest request, CancellationToken cancellationToken);
}

public sealed class HttpStockCountApprovalClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider internalTokenProvider) : IStockCountApprovalClient
{
    public async Task<StockCountApprovalResult> StartApprovalAsync(StockCountApprovalRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/business/v1/approvals/chains")
        {
            Content = JsonContent.Create(new StartApprovalChainHttpRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.TemplateCode,
                request.SourceService,
                request.DocumentType,
                request.DocumentId,
                null,
                request.StartedBy,
                request.Amount,
                request.OrganizationId,
                null)),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<StartApprovalChainHttpResponse>>(cancellationToken);
        if (envelope is null || !envelope.Success || envelope.Data is null)
        {
            throw new KnownException(envelope?.Message ?? "BusinessApproval did not return an approval chain id.");
        }

        return new StockCountApprovalResult(envelope.Data.ChainId);
    }

    private sealed record StartApprovalChainHttpRequest(
        string OrganizationId,
        string EnvironmentId,
        string TemplateCode,
        string SourceService,
        string DocumentType,
        string DocumentId,
        string? DocumentLineId,
        string StartedBy,
        decimal? Amount,
        string? RoutingOrganizationId,
        string? DepartmentId);

    private sealed record StartApprovalChainHttpResponse(string ChainId);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
