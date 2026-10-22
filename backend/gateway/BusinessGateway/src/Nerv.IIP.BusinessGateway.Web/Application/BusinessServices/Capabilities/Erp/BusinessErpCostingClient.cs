using System.Net;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>ERP 成本核算读写：工作中心人工费率、机器制造费用率、工单成本与差异、机器制造费用月度核对。</summary>
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

    Task<BusinessConsoleConfigureErpWorkCenterMachineOverheadRateResponse> ConfigureWorkCenterMachineOverheadRateAsync(
        string internalBearerToken,
        BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<BusinessConsoleErpWorkCenterMachineOverheadRateListResponse> ListWorkCenterMachineOverheadRatesAsync(
        string internalBearerToken,
        BusinessConsoleListErpWorkCenterMachineOverheadRatesRequest request,
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

    public async Task<BusinessConsoleConfigureErpWorkCenterMachineOverheadRateResponse> ConfigureWorkCenterMachineOverheadRateAsync(
        string internalBearerToken,
        BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequest request,
        string actor,
        CancellationToken cancellationToken)
    {
        // ERP 这组端点的适用性枚举按整数序列化，网关对外用字符串值，这里逐字段映射。
        var response = await SendAsync<DownstreamConfigureWorkCenterMachineOverheadRateResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/erp/finance/work-center-machine-overhead-rates",
            new DownstreamConfigureWorkCenterMachineOverheadRateRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkCenterId,
                request.AccountingPeriodCode,
                (int)request.Applicability,
                request.FixedOverheadBudget,
                request.VariableOverheadBudget,
                request.NormalCapacityMachineHours,
                request.CurrencyCode,
                request.Reason),
            cancellationToken,
            configureRequest: message =>
                message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", actor));

        if (!Guid.TryParse(response.WorkCenterMachineOverheadRateId, out var rateId) || rateId == Guid.Empty)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleConfigureErpWorkCenterMachineOverheadRateResponse(rateId.ToString());
    }

    public async Task<BusinessConsoleErpWorkCenterMachineOverheadRateListResponse> ListWorkCenterMachineOverheadRatesAsync(
        string internalBearerToken,
        BusinessConsoleListErpWorkCenterMachineOverheadRatesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamWorkCenterMachineOverheadRateList>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/erp/finance/work-center-machine-overhead-rates?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("workCenterId", request.WorkCenterId),
                ("accountingPeriodCode", request.AccountingPeriodCode),
                ("pageNumber", request.PageNumber),
                ("pageSize", request.PageSize)),
            null,
            cancellationToken);

        return new BusinessConsoleErpWorkCenterMachineOverheadRateListResponse(
            response.OrganizationId,
            response.EnvironmentId,
            response.WorkCenterId,
            response.AccountingPeriodCode,
            response.CurrentRevision,
            response.PageNumber,
            response.PageSize,
            response.TotalCount,
            response.Items.Select(item => new BusinessConsoleErpWorkCenterMachineOverheadRateItem(
                item.WorkCenterMachineOverheadRateId,
                item.AccountingPeriodCode,
                (BusinessConsoleErpMachineOverheadApplicability)item.Applicability,
                item.FixedOverheadBudget,
                item.VariableOverheadBudget,
                item.NormalCapacityMachineHours,
                item.FixedHourlyRate,
                item.VariableHourlyRate,
                item.TotalHourlyRate,
                item.CurrencyCode,
                item.Revision,
                item.ChangedBy,
                item.Reason,
                item.ChangedAtUtc)).ToArray());
    }

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

    private sealed record DownstreamConfigureWorkCenterMachineOverheadRateRequest(
        string OrganizationId,
        string EnvironmentId,
        string WorkCenterId,
        string AccountingPeriodCode,
        int Applicability,
        decimal FixedOverheadBudget,
        decimal VariableOverheadBudget,
        decimal NormalCapacityMachineHours,
        string CurrencyCode,
        string Reason);

    private sealed record DownstreamConfigureWorkCenterMachineOverheadRateResponse(
        string? WorkCenterMachineOverheadRateId);

    private sealed record DownstreamWorkCenterMachineOverheadRateList(
        string OrganizationId,
        string EnvironmentId,
        string WorkCenterId,
        string AccountingPeriodCode,
        int? CurrentRevision,
        int PageNumber,
        int PageSize,
        int TotalCount,
        IReadOnlyCollection<DownstreamWorkCenterMachineOverheadRateItem> Items);

    private sealed record DownstreamWorkCenterMachineOverheadRateItem(
        string WorkCenterMachineOverheadRateId,
        string AccountingPeriodCode,
        int Applicability,
        decimal FixedOverheadBudget,
        decimal VariableOverheadBudget,
        decimal NormalCapacityMachineHours,
        decimal FixedHourlyRate,
        decimal VariableHourlyRate,
        decimal TotalHourlyRate,
        string CurrencyCode,
        int Revision,
        string ChangedBy,
        string Reason,
        DateTimeOffset ChangedAtUtc);
}
