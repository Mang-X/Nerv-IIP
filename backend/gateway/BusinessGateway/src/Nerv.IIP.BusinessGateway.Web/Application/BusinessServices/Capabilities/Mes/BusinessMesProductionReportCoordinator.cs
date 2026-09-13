using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

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
    private const int PrintBatchPageSize = 500;

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
        var reportIntentFingerprint = string.IsNullOrWhiteSpace(request.LabelTemplateId)
            ? null
            : CreateReportIntentFingerprint(request);
        var existingBatch = reportIntentFingerprint is null
            ? null
            : await FindExistingBatchAsync(
                internalBearerToken,
                request,
                cancellationToken);
        if (existingBatch is not null)
        {
            return await RecordSerialReportAsync(
                internalBearerToken,
                request,
                actor,
                existingBatch,
                reportIntentFingerprint!,
                cancellationToken);
        }

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
                quantity,
                reportIntentFingerprint!),
            cancellationToken);
        var batchRequest = new BusinessConsoleBarcodePrintBatchRequest(
            request.OrganizationId,
            request.EnvironmentId,
            created.PrintBatchId);
        var reserved = (await barcodeLabel.GetPrintBatchAsync(
            internalBearerToken,
            batchRequest,
            cancellationToken)).PrintBatch;

        return await RecordSerialReportAsync(
            internalBearerToken,
            request,
            actor,
            reserved,
            reportIntentFingerprint!,
            cancellationToken);
    }

    private async Task<BusinessConsoleRecordProductionReportResponse> RecordSerialReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        string actor,
        BusinessConsoleBarcodePrintBatchDetail reserved,
        string reportIntentFingerprint,
        CancellationToken cancellationToken)
    {
        var quantity = ProductionSerialQuantity(request.GoodQuantity);
        var serials = ValidateReservedBatch(reserved, request, quantity, reportIntentFingerprint);
        var batchRequest = new BusinessConsoleBarcodePrintBatchRequest(
            request.OrganizationId,
            request.EnvironmentId,
            reserved.PrintBatchId);

        var report = await mes.RecordProductionReportAsync(
            internalBearerToken,
            AuthoritativeRequest(request, OnProductionPolicy, serials),
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

    private async Task<BusinessConsoleBarcodePrintBatchDetail?> FindExistingBatchAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        CancellationToken cancellationToken)
    {
        var skip = 0;
        while (true)
        {
            var page = await barcodeLabel.ListPrintBatchesAsync(
                internalBearerToken,
                new BusinessConsoleBarcodePrintBatchListRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    WorkOrderSource,
                    request.WorkOrderId,
                    Skip: skip,
                    Take: PrintBatchPageSize),
                cancellationToken);
            var existing = page.PrintBatches.SingleOrDefault(x =>
                string.Equals(x.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal));
            if (existing is not null)
            {
                return (await barcodeLabel.GetPrintBatchAsync(
                    internalBearerToken,
                    new BusinessConsoleBarcodePrintBatchRequest(
                        request.OrganizationId,
                        request.EnvironmentId,
                        existing.PrintBatchId),
                    cancellationToken)).PrintBatch;
            }

            skip += page.PrintBatches.Count;
            if (skip >= page.Total)
            {
                return null;
            }
            if (page.PrintBatches.Count == 0)
            {
                throw InvalidResponse();
            }
        }
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
        catch (BusinessServiceProxyException exception) when (MayHaveUnknownOutcome(exception))
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
        catch (BusinessServiceProxyException exception) when (MayHaveUnknownOutcome(exception))
        {
            return reserved;
        }
    }

    private static IReadOnlyCollection<string> ValidateReservedBatch(
        BusinessConsoleBarcodePrintBatchDetail batch,
        BusinessConsoleRecordProductionReportRequest request,
        int quantity,
        string reportIntentFingerprint)
    {
        if (!string.Equals(batch.SourceDocumentType, WorkOrderSource, StringComparison.Ordinal) ||
            !string.Equals(batch.SourceDocumentId, request.WorkOrderId, StringComparison.Ordinal) ||
            !string.Equals(batch.ReportIntentKey, request.IdempotencyKey, StringComparison.Ordinal))
        {
            throw InvalidResponse();
        }
        if (!string.Equals(batch.LabelTemplateId, request.LabelTemplateId, StringComparison.Ordinal) ||
            batch.RequestedQuantity != quantity ||
            !string.Equals(batch.ReportIntentFingerprint, reportIntentFingerprint, StringComparison.Ordinal))
        {
            throw IdempotencyConflict();
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

    private static string CreateReportIntentFingerprint(BusinessConsoleRecordProductionReportRequest request)
    {
        var consumedMaterialLots = (request.ConsumedMaterialLots ?? [])
            .Select(x => new ReportIntentConsumedMaterialLot(
                x.MaterialId,
                x.MaterialLotId,
                x.ConsumedQuantity,
                x.MaterialIssueRequestNo))
            .OrderBy(x => x.MaterialId, StringComparer.Ordinal)
            .ThenBy(x => x.MaterialLotId, StringComparer.Ordinal)
            .ThenBy(x => x.ConsumedQuantity)
            .ThenBy(x => x.MaterialIssueRequestNo, StringComparer.Ordinal)
            .ToArray();
        var intent = new ReportIntent(
            request.WorkOrderId,
            request.OperationTaskId,
            request.GoodQuantity,
            request.ScrapQuantity,
            request.ReworkQuantity,
            request.CompletesOperation,
            request.ReportedAtUtc,
            consumedMaterialLots,
            request.ScrapReasonCode,
            request.DefectRecordNo,
            request.ProducedLotNo,
            request.LabelTemplateId!);
        var canonicalJson = JsonSerializer.SerializeToUtf8Bytes(intent);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(canonicalJson)).ToLowerInvariant();
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

    private static bool MayHaveUnknownOutcome(BusinessServiceProxyException exception) =>
        (int)exception.StatusCode >= 500;

    private static BusinessServiceProxyException IdempotencyConflict() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.Conflict, "idempotency-conflict");

    private static BusinessServiceProxyException InvalidResponse() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "downstream-invalid-response");

    private sealed record ReportIntent(
        string WorkOrderId,
        string OperationTaskId,
        decimal GoodQuantity,
        decimal ScrapQuantity,
        decimal ReworkQuantity,
        bool CompletesOperation,
        DateTimeOffset ReportedAtUtc,
        IReadOnlyCollection<ReportIntentConsumedMaterialLot> ConsumedMaterialLots,
        string? ScrapReasonCode,
        string? DefectRecordNo,
        string? ProducedLotNo,
        string LabelTemplateId);

    private sealed record ReportIntentConsumedMaterialLot(
        string MaterialId,
        string MaterialLotId,
        decimal ConsumedQuantity,
        string MaterialIssueRequestNo);
}
