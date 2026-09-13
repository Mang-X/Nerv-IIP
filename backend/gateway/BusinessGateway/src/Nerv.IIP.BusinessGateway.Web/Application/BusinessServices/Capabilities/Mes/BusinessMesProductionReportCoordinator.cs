using System.Net;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMesProductionReportCoordinator
{
    Task<BusinessConsoleRecordProductionReportResponse> RecordAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        string actor,
        CancellationToken cancellationToken);
}

public sealed class BusinessMesProductionReportCoordinator(
    IBusinessMesClient mes,
    IBusinessMasterDataClient masterData,
    IBusinessBarcodeLabelClient barcodeLabel)
    : IBusinessMesProductionReportCoordinator
{
    private const string OnProductionPolicy = "on-production";
    private const string WorkOrderSource = "work-order";

    private static readonly HashSet<string> SupportedPolicies = new(StringComparer.Ordinal)
    {
        "none",
        "on-receipt",
        OnProductionPolicy,
        "on-shipment",
    };

    public async Task<BusinessConsoleRecordProductionReportResponse> RecordAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        string actor,
        CancellationToken cancellationToken)
    {
        var context = new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId);
        var workOrder = await mes.GetWorkOrderDetailAsync(
            internalBearerToken,
            request.WorkOrderId,
            context,
            cancellationToken);
        var sku = await masterData.GetResourceDetailAsync(
            internalBearerToken,
            new BusinessConsoleMasterDataResourceRequest(
                request.OrganizationId,
                request.EnvironmentId,
                "sku",
                workOrder.SkuId),
            cancellationToken);
        var policy = sku.SerialTrackingPolicy;
        if (!sku.Active || string.IsNullOrWhiteSpace(policy) || !SupportedPolicies.Contains(policy))
        {
            throw InvalidRequest("production-serial-policy-invalid");
        }

        if (!string.Equals(policy, OnProductionPolicy, StringComparison.Ordinal) || request.GoodQuantity == 0)
        {
            return await mes.RecordProductionReportAsync(
                internalBearerToken,
                AuthoritativeRequest(request, policy, []),
                actor,
                cancellationToken);
        }

        var quantity = ProductionSerialQuantity(request.GoodQuantity);
        if (string.IsNullOrWhiteSpace(request.LabelTemplateId))
        {
            throw InvalidRequest("production-label-template-required");
        }
        if (string.IsNullOrWhiteSpace(sku.DefaultBarcodeRuleCode))
        {
            throw InvalidRequest("production-label-rule-unavailable");
        }

        var rules = await barcodeLabel.ListRulesAsync(
            internalBearerToken,
            new BusinessConsoleBarcodeRuleListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                Status: "active",
                Keyword: sku.DefaultBarcodeRuleCode,
                Take: 500),
            cancellationToken);
        var matchingRules = rules.Rules
            .Where(x =>
                string.Equals(x.RuleCode, sku.DefaultBarcodeRuleCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                x.AllowedSourceDocumentTypes.Contains(WorkOrderSource, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (matchingRules.Length != 1)
        {
            throw InvalidRequest("production-label-rule-unavailable");
        }

        var created = await barcodeLabel.CreatePrintBatchAsync(
            internalBearerToken,
            new BusinessConsoleCreateBarcodePrintBatchRequest(
                request.OrganizationId,
                request.EnvironmentId,
                matchingRules[0].BarcodeRuleId,
                request.LabelTemplateId,
                WorkOrderSource,
                request.WorkOrderId,
                request.IdempotencyKey,
                "{}",
                quantity),
            cancellationToken);
        var batchRequest = new BusinessConsoleBarcodePrintBatchRequest(
            request.OrganizationId,
            request.EnvironmentId,
            created.PrintBatchId);
        var reserved = (await barcodeLabel.GetPrintBatchAsync(
            internalBearerToken,
            batchRequest,
            cancellationToken)).PrintBatch;
        var serials = ValidateReservedBatch(reserved, request, quantity);

        var report = await mes.RecordProductionReportAsync(
            internalBearerToken,
            AuthoritativeRequest(request, policy, serials),
            actor,
            cancellationToken);

        var latest = await ActivateAndReadAsync(
            internalBearerToken,
            batchRequest,
            report,
            reserved,
            cancellationToken);
        var activationConverged =
            string.Equals(latest.ProductionReportId, report.ProductionReportId, StringComparison.Ordinal) &&
            string.Equals(latest.ProductionReportNo, report.ReportNo, StringComparison.Ordinal);
        return new BusinessConsoleRecordProductionReportResponse(
            report.ProductionReportId,
            report.ReportNo,
            serials,
            report.OperationReceipt,
            latest.PrintBatchId,
            latest.Status,
            !activationConverged);
    }

    private async Task<BusinessConsoleBarcodePrintBatchDetail> ActivateAndReadAsync(
        string internalBearerToken,
        BusinessConsoleBarcodePrintBatchRequest batchRequest,
        BusinessConsoleRecordProductionReportResponse report,
        BusinessConsoleBarcodePrintBatchDetail reserved,
        CancellationToken cancellationToken)
    {
        try
        {
            await barcodeLabel.ActivatePrintBatchAsync(
                internalBearerToken,
                new BusinessConsoleActivateBarcodePrintBatchRequest(
                    batchRequest.PrintBatchId,
                    batchRequest.OrganizationId,
                    batchRequest.EnvironmentId,
                    new BusinessConsoleActivateBarcodePrintBatchBody(
                        report.ProductionReportId,
                        report.ReportNo)),
                cancellationToken);
        }
        catch (BusinessServiceProxyException)
        {
            // 激活响应未知时只读回同一批次；不重建批次、不派工，也不把已成功的 MES 报工伪装成失败。
        }

        try
        {
            return (await barcodeLabel.GetPrintBatchAsync(
                internalBearerToken,
                batchRequest,
                cancellationToken)).PrintBatch;
        }
        catch (BusinessServiceProxyException)
        {
            return reserved;
        }
    }

    private static IReadOnlyCollection<string> ValidateReservedBatch(
        BusinessConsoleBarcodePrintBatchDetail batch,
        BusinessConsoleRecordProductionReportRequest request,
        int quantity)
    {
        if (!string.Equals(batch.SourceDocumentType, WorkOrderSource, StringComparison.Ordinal) ||
            !string.Equals(batch.SourceDocumentId, request.WorkOrderId, StringComparison.Ordinal) ||
            !string.Equals(batch.ReportIntentKey, request.IdempotencyKey, StringComparison.Ordinal) ||
            batch.RequestedQuantity != quantity)
        {
            throw InvalidResponse();
        }

        var serials = batch.Items
            .OrderBy(x => x.SequenceNo)
            .Select(x => x.SerialNumber)
            .ToArray();
        if (serials.Length != quantity ||
            serials.Any(string.IsNullOrWhiteSpace) ||
            serials.Distinct(StringComparer.Ordinal).Count() != serials.Length)
        {
            throw InvalidResponse();
        }

        return serials!;
    }

    private static int ProductionSerialQuantity(decimal goodQuantity)
    {
        if (goodQuantity <= 0 || goodQuantity != decimal.Truncate(goodQuantity) || goodQuantity > int.MaxValue)
        {
            throw InvalidRequest("production-label-quantity-invalid");
        }

        return decimal.ToInt32(goodQuantity);
    }

    private static BusinessConsoleRecordProductionReportRequest AuthoritativeRequest(
        BusinessConsoleRecordProductionReportRequest request,
        string policy,
        IReadOnlyCollection<string> serials) =>
        request with
        {
            SerialNo = null,
            SerialTrackingPolicy = policy,
            SerialNumbers = serials,
        };

    private static BusinessServiceProxyException InvalidRequest(string code) =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadRequest, code);

    private static BusinessServiceProxyException InvalidResponse() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "downstream-invalid-response");
}
