using System.Net;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>ERP 成本核算读写：工作中心人工费率、工单成本与差异、机器制造费用月度核对。</summary>
public interface IBusinessErpCostingClient
{
    Task<BusinessConsoleConfigureErpWorkCenterCostRateResponse> ConfigureWorkCenterCostRateAsync(
        string internalBearerToken,
        BusinessConsoleConfigureErpWorkCenterCostRateRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpWorkCenterCostRateListResponse> ListWorkCenterCostRatesAsync(
        string internalBearerToken,
        BusinessConsoleListErpWorkCenterCostRatesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpWorkOrderCostListResponse> ListWorkOrderCostsAsync(
        string internalBearerToken,
        BusinessConsoleListErpWorkOrderCostsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpWorkOrderCostVarianceResponse> GetWorkOrderCostVarianceAsync(
        string internalBearerToken,
        BusinessConsoleGetErpWorkOrderCostVarianceRequest request,
        CancellationToken cancellationToken);

    /// <summary>该工单在 ERP 的成本归集进度；ERP 尚无该工单的成本记录时返回 null。</summary>
    Task<BusinessConsoleMesReceiptCostCapitalizationProgress?> GetWorkOrderCostProgressAsync(
        string internalBearerToken,
        BusinessConsoleErpWorkOrderCostProgressRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpMachineOverheadReconciliationListResponse> ListMachineOverheadReconciliationsAsync(
        string internalBearerToken,
        BusinessConsoleListErpMachineOverheadReconciliationsRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessErpCostingClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessErpCostingClient
{
    public async Task<BusinessConsoleConfigureErpWorkCenterCostRateResponse> ConfigureWorkCenterCostRateAsync(
        string internalBearerToken,
        BusinessConsoleConfigureErpWorkCenterCostRateRequest request,
        string actor,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamConfigureWorkCenterCostRateResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/work-center-cost-rates",
            request,
            cancellationToken,
            configureRequest: message =>
                message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", actor));

        if (!Guid.TryParse(response.WorkCenterCostRateId, out var workCenterCostRateId)
            || workCenterCostRateId == Guid.Empty)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleConfigureErpWorkCenterCostRateResponse(
            workCenterCostRateId.ToString());
    }

    public Task<BusinessConsoleErpWorkCenterCostRateListResponse> ListWorkCenterCostRatesAsync(
        string internalBearerToken,
        BusinessConsoleListErpWorkCenterCostRatesRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpWorkCenterCostRateListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/work-center-cost-rates?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("workCenterId", request.WorkCenterId),
                ("atUtc", request.AtUtc)),
            null,
            cancellationToken);

    public Task<BusinessConsoleErpWorkOrderCostListResponse> ListWorkOrderCostsAsync(
        string internalBearerToken,
        BusinessConsoleListErpWorkOrderCostsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleErpWorkOrderCostListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/work-order-costs?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public async Task<BusinessConsoleMesReceiptCostCapitalizationProgress?> GetWorkOrderCostProgressAsync(
        string internalBearerToken,
        BusinessConsoleErpWorkOrderCostProgressRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamWorkOrderCostListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/work-order-costs?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("workOrderId", request.WorkOrderId),
                ("take", 1)),
            null,
            cancellationToken);
        var cost = response.Items.SingleOrDefault();
        return cost is null
            ? null
            : new BusinessConsoleMesReceiptCostCapitalizationProgress(
                cost.CompletedAtUtc.HasValue,
                cost.ReceivedReportCount,
                cost.ExpectedReportCount,
                cost.ReceivedMaterialMovementCount,
                cost.ExpectedMaterialMovementCount,
                cost.CapitalizationPublished);
    }

    public async Task<BusinessConsoleErpWorkOrderCostVarianceResponse> GetWorkOrderCostVarianceAsync(
        string internalBearerToken,
        BusinessConsoleGetErpWorkOrderCostVarianceRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleErpWorkOrderCostVarianceResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/erp/finance/work-order-costs/{Uri.EscapeDataString(request.WorkOrderId)}?" + Query(
                ("pageNumber", request.PageNumber),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken,
            configureRequest: message => AddMachineOverheadScopeHeaders(message, request.OrganizationId, request.EnvironmentId));

        ValidateMachineOverheadState(
            response.MachineCostStatus,
            response.MachineCostUnavailableReason,
            response.ActualMachineHours,
            response.AppliedFixedMachineOverhead,
            response.AppliedVariableMachineOverhead,
            response.AppliedMachineOverheadTotal);
        foreach (var operation in response.MachineOverheadOperations)
        {
            ValidateMachineOverheadState(
                operation.Status,
                operation.UnavailableReason,
                operation.ActualMachineHours,
                operation.AppliedFixedMachineOverhead,
                operation.AppliedVariableMachineOverhead,
                operation.AppliedMachineOverheadTotal);
        }

        return response;
    }

    public async Task<BusinessConsoleErpMachineOverheadReconciliationListResponse> ListMachineOverheadReconciliationsAsync(
        string internalBearerToken,
        BusinessConsoleListErpMachineOverheadReconciliationsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleErpMachineOverheadReconciliationListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/work-center-machine-overhead-reconciliations?" + Query(
                ("accountingPeriodCode", request.AccountingPeriodCode),
                ("workCenterId", request.WorkCenterId),
                ("pageNumber", request.PageNumber),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken,
            configureRequest: message => AddMachineOverheadScopeHeaders(message, request.OrganizationId, request.EnvironmentId));

        ValidateMachineOverheadStatus(response.ReconciliationStatus, response.ReconciliationUnavailableReason);
        foreach (var item in response.Items)
            ValidateMachineOverheadStatus(item.ReconciliationStatus, item.UnavailableReason);

        return response;
    }

    private static void AddMachineOverheadScopeHeaders(
        HttpRequestMessage request,
        string organizationId,
        string environmentId)
    {
        request.Headers.TryAddWithoutValidation("X-Organization-Id", organizationId);
        request.Headers.TryAddWithoutValidation("X-Environment-Id", environmentId);
    }

    private static void ValidateMachineOverheadState(
        BusinessConsoleMachineOverheadReadStatus status,
        string? unavailableReason,
        params decimal?[] amounts)
    {
        ValidateMachineOverheadStatus(status, unavailableReason);
        var valid = status switch
        {
            BusinessConsoleMachineOverheadReadStatus.Available =>
                amounts.All(amount => amount.HasValue),
            BusinessConsoleMachineOverheadReadStatus.NotApplicable or
                BusinessConsoleMachineOverheadReadStatus.Unavailable =>
                amounts.All(amount => !amount.HasValue),
            _ => false,
        };

        if (!valid)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private static void ValidateMachineOverheadStatus(
        BusinessConsoleMachineOverheadReadStatus status,
        string? unavailableReason)
    {
        var valid = status switch
        {
            BusinessConsoleMachineOverheadReadStatus.Available => unavailableReason is null,
            BusinessConsoleMachineOverheadReadStatus.NotApplicable or
                BusinessConsoleMachineOverheadReadStatus.Unavailable => !string.IsNullOrWhiteSpace(unavailableReason),
            _ => false,
        };

        if (!valid)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private sealed record DownstreamWorkOrderCostListResponse(IReadOnlyCollection<DownstreamWorkOrderCostItem> Items);

    private sealed record DownstreamWorkOrderCostItem(
        DateTimeOffset? CompletedAtUtc,
        int ExpectedReportCount,
        int ReceivedReportCount,
        int ExpectedMaterialMovementCount,
        int ReceivedMaterialMovementCount,
        bool CapitalizationPublished);

    private sealed record DownstreamConfigureWorkCenterCostRateResponse(
        string? WorkCenterCostRateId);
}
