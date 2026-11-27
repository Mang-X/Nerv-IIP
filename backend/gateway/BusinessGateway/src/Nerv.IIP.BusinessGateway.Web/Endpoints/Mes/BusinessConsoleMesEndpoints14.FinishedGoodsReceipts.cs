using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/finished-goods-receipt-requests")]
[BusinessGatewayOperationId("listBusinessConsoleMesFinishedGoodsReceiptRequests")]
public sealed class ListBusinessConsoleMesFinishedGoodsReceiptRequestsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessErpCostingClient erp,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesReceiptRequestListResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesReceiptRequestListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var response = await mes.ListFinishedGoodsReceiptRequestsAsync(tokenProvider.BearerToken, request, cancellationToken);

        // #3728：Requested 且尚无单位成本 = 在等 ERP 成本归集。把 ERP 的归集进度挂到行上，
        // 让「报工成本 0/8 不前进」在入库单上直接可见。进度是 ERP 财务数据，按调用者自身的
        // ERP 财务读权限决定是否挂（内部 token 不提升最终主体权限）；无权限时行保持原样。
        var awaitingWorkOrderIds = response.Items
            .Where(IsAwaitingCostCapitalization)
            .Select(item => item.WorkOrderId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (awaitingWorkOrderIds.Length == 0)
        {
            return response;
        }

        var financeRead = await AuthorizationClient.CheckAsync(
            bearerToken,
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.ErpFinanceRead,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                null),
            cancellationToken);
        if (!financeRead.IsAllowed)
        {
            return response;
        }

        var progress = await Task.WhenAll(awaitingWorkOrderIds.Select(ReadProgressAsync));
        var progressByWorkOrder = progress
            .Where(x => x.Progress is not null)
            .ToDictionary(x => x.WorkOrderId, x => x.Progress!, StringComparer.Ordinal);
        return response with
        {
            Items = response.Items
                .Select(item => IsAwaitingCostCapitalization(item)
                    ? item with { CostCapitalization = progressByWorkOrder.GetValueOrDefault(item.WorkOrderId) }
                    : item)
                .ToArray(),
        };

        async Task<(string WorkOrderId, BusinessConsoleMesReceiptCostCapitalizationProgress? Progress)> ReadProgressAsync(
            string workOrderId)
        {
            try
            {
                var read = await erp.GetWorkOrderCostProgressAsync(
                    tokenProvider.BearerToken,
                    new BusinessConsoleErpWorkOrderCostProgressRequest(request.OrganizationId, request.EnvironmentId, workOrderId),
                    cancellationToken);
                // ERP 还没有该工单的成本记录 = 一条报工成本都没收到、也没收到完工。
                return (workOrderId, read ?? new BusinessConsoleMesReceiptCostCapitalizationProgress(false, 0, 0, 0, 0, false));
            }
            catch (BusinessServiceProxyException)
            {
                // 进度只是附加说明：ERP 读不到时这些行不挂进度（与无权限同一条路），入库单列表本身照常返回。
                return (workOrderId, null);
            }
        }
    }

    private static bool IsAwaitingCostCapitalization(BusinessConsoleMesReceiptRequestRow item) =>
        item.UnitCost is null
        && string.Equals(item.ReceiptStatus, "Requested", StringComparison.Ordinal);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/finished-goods-receipt-requests/{requestNo}/inventory-link")]
[BusinessGatewayOperationId("getBusinessConsoleMesFinishedGoodsReceiptInventoryLink")]
public sealed class GetBusinessConsoleMesFinishedGoodsReceiptInventoryLinkEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessInventoryClient inventory,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesFinishedGoodsInventoryLinkRequest, BusinessConsoleMesFinishedGoodsInventoryLinkResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsRead)
{
    private const string MesSourceService = "business-mes";

    protected override string OrganizationId(BusinessConsoleMesFinishedGoodsInventoryLinkRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesFinishedGoodsInventoryLinkRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleMesFinishedGoodsInventoryLinkResponse> ForwardAsync(
        BusinessConsoleMesFinishedGoodsInventoryLinkRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var inventoryAuthorization = await AuthorizationClient.CheckAsync(
            bearerToken,
            new BusinessGatewayPermissionRequirement(
                BusinessGatewayPermissions.InventoryLedgerRead,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                null),
            cancellationToken);
        if (!inventoryAuthorization.IsAllowed)
        {
            throw BusinessServiceProxyException.FromLocalRejection(
                System.Net.HttpStatusCode.Forbidden,
                BusinessGatewayLocalRejectionStableWireCodes.Forbidden);
        }

        var receipts = await mes.ListFinishedGoodsReceiptRequestsAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleMesListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                WorkOrderId: request.WorkOrderId,
                Take: 2),
            cancellationToken,
            request.RequestNo);
        var receipt = receipts.Items.SingleOrDefault(item =>
            string.Equals(item.RequestNo, request.RequestNo, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(request.WorkOrderId)
                || string.Equals(item.WorkOrderId, request.WorkOrderId, StringComparison.Ordinal)));
        if (receipt is null)
        {
            throw BusinessServiceProxyException.FromLocalRejection(System.Net.HttpStatusCode.NotFound, BusinessGatewayLocalRejectionStableWireCodes.FinishedGoodsReceiptNotFound);
        }

        var stock = await inventory.GetStockBySourceAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleInventoryStockBySourceRequest(
                request.OrganizationId,
                request.EnvironmentId,
                MesSourceService,
                receipt.RequestNo,
                receipt.WorkOrderId),
            cancellationToken);

        return new BusinessConsoleMesFinishedGoodsInventoryLinkResponse(
            LinkStatus(receipt, stock),
            receipt.RequestNo,
            receipt.WorkOrderId,
            receipt.WorkOrderNo,
            receipt.SkuId,
            receipt.SkuCode,
            receipt.ProducedLotNo,
            receipt.SerialNo,
            receipt.Quantity,
            receipt.PostedQuantity,
            receipt.RemainingQuantity,
            receipt.ReceiptStatus,
            receipt.PostedInventoryMovementId,
            receipt.PostedAtUtc,
            receipt.InventoryPostingFailureCode,
            receipt.InventoryPostingFailureMessage,
            receipt.InventoryPostingFailedAtUtc,
            stock.SourceService,
            stock.SourceDocumentId ?? receipt.RequestNo,
            stock.SourceDocumentLineId ?? receipt.WorkOrderId,
            stock.IsEstablished,
            stock.Movements,
            stock.Balances);
    }

    private static string LinkStatus(
        BusinessConsoleMesReceiptRequestRow receipt,
        BusinessConsoleInventoryStockBySourceResponse stock)
    {
        if (string.Equals(receipt.ReceiptStatus, "InventoryPostingFailed", StringComparison.OrdinalIgnoreCase))
        {
            return stock.IsEstablished ? "partiallyPosted" : "postingFailed";
        }

        if (!stock.IsEstablished)
        {
            return "notPosted";
        }

        if (stock.Balances.Any(balance =>
                balance.OnHandQuantity > 0
                && !string.Equals(balance.QualityStatus, "unrestricted", StringComparison.OrdinalIgnoreCase)))
        {
            return "qualityRestricted";
        }

        if (string.Equals(receipt.ReceiptStatus, "PartiallyPosted", StringComparison.OrdinalIgnoreCase)
            || receipt.RemainingQuantity > 0)
        {
            return "partiallyPosted";
        }

        return "posted";
    }
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/finished-goods-receipt-requests")]
[BusinessGatewayOperationId("createBusinessConsoleMesFinishedGoodsReceiptRequest")]
public sealed class CreateBusinessConsoleMesFinishedGoodsReceiptRequestEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCreateReceiptRequest, BusinessConsoleMesCreateReceiptResponse>(
        auth,
        BusinessGatewayPermissions.MesReceiptsManage)
{
    protected override string OrganizationId(BusinessConsoleMesCreateReceiptRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCreateReceiptRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesCreateReceiptResponse> ForwardAsync(
        BusinessConsoleMesCreateReceiptRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.CreateFinishedGoodsReceiptRequestAsync(tokenProvider.BearerToken, request, cancellationToken);
}

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 CreateFinishedGoodsReceiptRequestCommandHandler 走 CodeAllocator，落 mes.code_idempotency_keys.idempotency_key(150)。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleMesCreateReceiptRequestValidator
    : Validator<BusinessConsoleMesCreateReceiptRequest>
{
    public BusinessConsoleMesCreateReceiptRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}
