using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.ServiceAuth;
using ContractManufacturingBomListItem = Nerv.IIP.Contracts.ProductEngineering.ManufacturingBomListItem;
using ContractStockAvailabilityResponse = Nerv.IIP.Contracts.Inventory.StockAvailabilityResponse;
using MesMaterialScanPrevalidationResponse = Nerv.IIP.Contracts.Mes.BusinessConsoleMesMaterialScanPrevalidationResponse;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

public sealed record PrevalidateMaterialScanQuery(
    string OrganizationId,
    string EnvironmentId,
    string MaterialIssueRequestId,
    string WorkOrderId,
    string OperationTaskId) : IQuery<MesMaterialScanPrevalidationResponse>;

public sealed class PrevalidateMaterialScanQueryValidator : AbstractValidator<PrevalidateMaterialScanQuery>
{
    public PrevalidateMaterialScanQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaterialIssueRequestId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationTaskId).NotEmpty().MaximumLength(100);
    }
}

public sealed record MesMaterialQualificationRequest(
    string OrganizationId,
    string EnvironmentId,
    string FinishedSkuId,
    string ProductionVersionId,
    IReadOnlyCollection<string> RequiredPrimaryMaterialIds,
    string MaterialId);

public interface IMesMaterialQualificationProvider
{
    Task<bool> IsFrozenSubstituteAsync(MesMaterialQualificationRequest request, CancellationToken cancellationToken);
}

public sealed record MesMaterialLotAvailabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    string MaterialId,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string MaterialLotId,
    DateOnly AsOfDate);

public sealed record MesMaterialLotAvailabilityResult(bool Exists, bool IsExpired, bool MovementAllowed);

public interface IMesMaterialLotAvailabilityProvider
{
    Task<MesMaterialLotAvailabilityResult> GetAsync(
        MesMaterialLotAvailabilityRequest request,
        CancellationToken cancellationToken);
}

public sealed class PrevalidateMaterialScanQueryHandler(
    ApplicationDbContext dbContext,
    IMesMaterialQualificationProvider qualificationProvider,
    IMesMaterialLotAvailabilityProvider availabilityProvider,
    TimeProvider timeProvider)
    : IQueryHandler<PrevalidateMaterialScanQuery, MesMaterialScanPrevalidationResponse>
{
    public async Task<MesMaterialScanPrevalidationResponse> Handle(
        PrevalidateMaterialScanQuery request,
        CancellationToken cancellationToken)
    {
        var evaluatedAtUtc = timeProvider.GetUtcNow();
        var issue = await dbContext.MaterialIssueRequests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.RequestNo == request.MaterialIssueRequestId, cancellationToken);
        if (issue is null)
        {
            return Reject(request, "material-issue-request-not-found", evaluatedAtUtc);
        }

        if (!string.Equals(issue.WorkOrderId, request.WorkOrderId, StringComparison.Ordinal))
        {
            return Reject(
                request, "work-order-mismatch", evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        if (!string.Equals(issue.OperationTaskId, request.OperationTaskId, StringComparison.Ordinal))
        {
            return Reject(
                request, "operation-task-mismatch", evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        var workOrder = await dbContext.WorkOrders.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.WorkOrderIdValue == request.WorkOrderId, cancellationToken);
        var operationExists = await dbContext.OperationTasks.AsNoTracking().AnyAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.WorkOrderId == request.WorkOrderId &&
            x.OperationTaskIdValue == request.OperationTaskId, cancellationToken);
        if (workOrder is null || !operationExists)
        {
            return Reject(
                request, "mes-context-not-found", evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        if (!string.Equals(issue.Status, MaterialIssueRequest.ReceivedStatus, StringComparison.Ordinal) ||
            issue.ReceivedQuantity < issue.RequestedQuantity ||
            string.IsNullOrWhiteSpace(issue.MaterialLotId) ||
            string.IsNullOrWhiteSpace(issue.TargetSiteCode) ||
            string.IsNullOrWhiteSpace(issue.TargetLocationCode))
        {
            return Reject(
                request, "line-side-receipt-incomplete", evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        var requiredPrimaryMaterialIds = await dbContext.MaterialRequirements.AsNoTracking().Where(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.WorkOrderId == request.WorkOrderId &&
            (x.OperationTaskId == null || x.OperationTaskId == request.OperationTaskId))
            .Select(x => x.MaterialId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var isPrimary = requiredPrimaryMaterialIds.Contains(issue.MaterialId, StringComparer.OrdinalIgnoreCase);

        var qualification = "primary";
        if (!isPrimary)
        {
            if (string.IsNullOrWhiteSpace(workOrder.ProductionVersionId) ||
                requiredPrimaryMaterialIds.Length == 0 ||
                !await qualificationProvider.IsFrozenSubstituteAsync(
                    new MesMaterialQualificationRequest(
                        request.OrganizationId,
                        request.EnvironmentId,
                        workOrder.SkuId,
                        workOrder.ProductionVersionId,
                        requiredPrimaryMaterialIds,
                        issue.MaterialId),
                    cancellationToken))
            {
                return Reject(
                    request, "material-not-required", evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
            }

            qualification = "substitute";
        }

        var availability = await availabilityProvider.GetAsync(
            new MesMaterialLotAvailabilityRequest(
                request.OrganizationId,
                request.EnvironmentId,
                issue.MaterialId,
                issue.UomCode,
                issue.TargetSiteCode,
                issue.TargetLocationCode,
                issue.MaterialLotId,
                DateOnly.FromDateTime(evaluatedAtUtc.UtcDateTime)),
            cancellationToken);
        var reasonCode = !availability.Exists
            ? "material-lot-not-found"
            : availability.IsExpired
                ? "material-lot-expired"
                : !availability.MovementAllowed
                    ? "material-lot-blocked"
                    : null;
        if (reasonCode is not null)
        {
            return Reject(
                request, reasonCode, evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        return new MesMaterialScanPrevalidationResponse(
            MesMaterialScanDecision.Accepted, "material-scan-accepted", request.MaterialIssueRequestId, request.WorkOrderId,
            request.OperationTaskId, issue.MaterialId, issue.MaterialLotId, qualification, evaluatedAtUtc);
    }

    private static MesMaterialScanPrevalidationResponse Reject(
        PrevalidateMaterialScanQuery request,
        string reasonCode,
        DateTimeOffset evaluatedAtUtc,
        string? materialId = null,
        string? materialLotId = null) =>
        new(MesMaterialScanDecision.Rejected, reasonCode, request.MaterialIssueRequestId, request.WorkOrderId,
            request.OperationTaskId, materialId, materialLotId, null, evaluatedAtUtc);
}

public sealed class HttpMesMaterialPrevalidationProvider(
    MesProductEngineeringHttpClient productEngineeringClient,
    MesInventoryHttpClient inventoryClient,
    IInternalServiceTokenProvider internalTokenProvider,
    IMesIntegrationEventContextAccessor correlationContextAccessor)
    : IMesMaterialQualificationProvider, IMesMaterialLotAvailabilityProvider
{
    public async Task<bool> IsFrozenSubstituteAsync(
        MesMaterialQualificationRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = correlationContextAccessor.GetContext().CorrelationId;
        var versions = await GetAsync<ListProductionVersionsResponse>(
            productEngineeringClient.HttpClient,
            "ProductEngineering",
            "/api/business/v1/engineering/production-versions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.FinishedSkuId),
                ("skip", 0),
                ("take", 500)),
            correlationId,
            cancellationToken);
        if (versions.Items is null ||
            versions.Total != versions.Items.Count ||
            versions.Items.Any(x => x is null ||
                string.IsNullOrWhiteSpace(x.ProductionVersionId) ||
                string.IsNullOrWhiteSpace(x.OrganizationId) ||
                string.IsNullOrWhiteSpace(x.EnvironmentId) ||
                string.IsNullOrWhiteSpace(x.SkuCode) ||
                string.IsNullOrWhiteSpace(x.MbomVersionId) ||
                string.IsNullOrWhiteSpace(x.RoutingVersionId) ||
                x.ValidFrom == default ||
                string.IsNullOrWhiteSpace(x.Status)))
        {
            throw SourceUnavailable("ProductEngineering 冻结生产版本列表不完整。");
        }

        var matches = versions.Items.Where(x =>
            string.Equals(x.ProductionVersionId, request.ProductionVersionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.OrganizationId, request.OrganizationId, StringComparison.Ordinal) &&
            string.Equals(x.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal) &&
            string.Equals(x.SkuCode, request.FinishedSkuId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
        {
            throw SourceUnavailable("ProductEngineering 冻结生产版本列表不完整或存在重复项。");
        }

        var version = matches.SingleOrDefault();
        if (version is null || !TryParseVersionReference(version.MbomVersionId, out var bomCode, out var revision))
        {
            return false;
        }

        var bom = await GetAsync<ContractManufacturingBomListItem>(
            productEngineeringClient.HttpClient,
            "ProductEngineering",
            $"/api/business/v1/engineering/manufacturing-boms/{Uri.EscapeDataString(bomCode)}/{Uri.EscapeDataString(revision)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            correlationId,
            cancellationToken);
        if (!string.Equals(bom.BomCode, bomCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(bom.Revision, revision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(bom.SkuCode, request.FinishedSkuId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(bom.Status, "published", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (bom.MaterialLines is null || bom.RecipeLines is null)
        {
            throw SourceUnavailable("ProductEngineering MBOM 明细集合不完整。");
        }

        var requiredPrimaryMaterialIds = request.RequiredPrimaryMaterialIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return bom.MaterialLines.Any(line =>
            requiredPrimaryMaterialIds.Contains(line.SkuCode) &&
            SplitSubstituteCodes(line.SubstituteSkuCodes).Contains(request.MaterialId, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<MesMaterialLotAvailabilityResult> GetAsync(
        MesMaterialLotAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var response = await GetAsync<ContractStockAvailabilityResponse>(
            inventoryClient.HttpClient,
            "Inventory",
            "/api/inventory/v1/availability?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.MaterialId),
                ("uomCode", request.UomCode),
                ("siteCode", request.SiteCode),
                ("locationCode", request.LocationCode),
                ("lotNo", request.MaterialLotId),
                ("asOfDate", request.AsOfDate)),
            correlationContextAccessor.GetContext().CorrelationId,
            cancellationToken);
        if (!string.Equals(response.OrganizationId, request.OrganizationId, StringComparison.Ordinal) ||
            !string.Equals(response.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal) ||
            !string.Equals(response.SkuCode, request.MaterialId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.UomCode, request.UomCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.SiteCode, request.SiteCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.LocationCode, request.LocationCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.LotNo, request.MaterialLotId, StringComparison.OrdinalIgnoreCase))
        {
            throw SourceUnavailable("Inventory 返回了与请求范围不一致的物料批次。");
        }

        if (response.Items is null || response.Items.Any(x => x is null ||
            !string.Equals(x.LocationCode, request.LocationCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(x.LotNo, request.MaterialLotId, StringComparison.OrdinalIgnoreCase)))
        {
            throw SourceUnavailable("Inventory 返回的库存明细集合不完整或超出请求范围。");
        }

        if (response.OnHandQuantity != response.Items.Sum(x => x.OnHandQuantity))
        {
            throw SourceUnavailable("Inventory 返回的汇总在手量与明细不一致。");
        }

        var lines = response.Items.Where(x =>
            string.Equals(x.LocationCode, request.LocationCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.LotNo, request.MaterialLotId, StringComparison.OrdinalIgnoreCase) &&
            x.OnHandQuantity > 0m).ToArray();
        return lines.Length == 0
            ? new MesMaterialLotAvailabilityResult(false, false, false)
            : new MesMaterialLotAvailabilityResult(true, lines.Any(x => x.IsExpired), lines.All(x => x.MovementAllowed));
    }

    private async Task<T> GetAsync<T>(
        HttpClient client,
        string serviceName,
        string requestUri,
        string correlationId,
        CancellationToken cancellationToken) where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw SourceUnavailable($"{serviceName} 暂不可用。{exception.Message}");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw SourceUnavailable($"{serviceName} 请求超时。{exception.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw SourceUnavailable($"{serviceName} 返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
            }

            ResponseDataEnvelope<T>? envelope;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
            }
            catch (JsonException exception)
            {
                throw SourceUnavailable($"{serviceName} 返回畸形 JSON。{exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                throw SourceUnavailable($"{serviceName} 返回不支持的内容类型。{exception.Message}");
            }
            if (envelope is null || !envelope.Success || envelope.Data is null)
            {
                throw SourceUnavailable($"{serviceName} 返回空响应或失败响应。");
            }

            return envelope.Data;
        }
    }

    private static KnownException SourceUnavailable(string detail) =>
        new($"MATERIAL_SCAN_SOURCE_UNAVAILABLE: 物料扫码预校验来源不可用。{detail}");

    private static IEnumerable<string> SplitSubstituteCodes(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool TryParseVersionReference(string value, out string code, out string revision)
    {
        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        code = parts.Length == 2 ? parts[0] : string.Empty;
        revision = parts.Length == 2 ? parts[1] : string.Empty;
        return !string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(revision);
    }

    private static string Query(params (string Name, object? Value)[] values) => string.Join('&', values.Select(x =>
        $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(Format(x.Value))}"));

    private static string Format(object? value) => value switch
    {
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
