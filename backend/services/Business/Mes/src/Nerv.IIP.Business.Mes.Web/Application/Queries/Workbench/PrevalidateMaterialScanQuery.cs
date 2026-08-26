using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.ServiceAuth;

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

public sealed record MesMaterialScanPrevalidationResponse(
    string Decision,
    string ReasonCode,
    string MaterialIssueRequestId,
    string WorkOrderId,
    string OperationTaskId,
    string? MaterialId,
    string? MaterialLotId,
    string? MaterialQualification,
    DateTimeOffset EvaluatedAtUtc)
{
    public static MesMaterialScanPrevalidationResponse Reject(
        PrevalidateMaterialScanQuery request,
        string reasonCode,
        DateTimeOffset evaluatedAtUtc,
        string? materialId = null,
        string? materialLotId = null) =>
        new("rejected", reasonCode, request.MaterialIssueRequestId, request.WorkOrderId,
            request.OperationTaskId, materialId, materialLotId, null, evaluatedAtUtc);
}

public sealed record MesMaterialQualificationRequest(
    string OrganizationId,
    string EnvironmentId,
    string FinishedSkuId,
    string ProductionVersionId,
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
            return MesMaterialScanPrevalidationResponse.Reject(request, "material-issue-request-not-found", evaluatedAtUtc);
        }

        if (!string.Equals(issue.WorkOrderId, request.WorkOrderId, StringComparison.Ordinal))
        {
            return MesMaterialScanPrevalidationResponse.Reject(
                request, "work-order-mismatch", evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        if (!string.Equals(issue.OperationTaskId, request.OperationTaskId, StringComparison.Ordinal))
        {
            return MesMaterialScanPrevalidationResponse.Reject(
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
            return MesMaterialScanPrevalidationResponse.Reject(
                request, "mes-context-not-found", evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        if (issue.ReceivedQuantity <= 0m ||
            string.IsNullOrWhiteSpace(issue.MaterialLotId) ||
            string.IsNullOrWhiteSpace(issue.TargetSiteCode) ||
            string.IsNullOrWhiteSpace(issue.TargetLocationCode))
        {
            return MesMaterialScanPrevalidationResponse.Reject(
                request, "line-side-receipt-incomplete", evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        var isPrimary = await dbContext.MaterialRequirements.AsNoTracking().AnyAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.WorkOrderId == request.WorkOrderId &&
            (x.OperationTaskId == null || x.OperationTaskId == request.OperationTaskId) &&
            x.MaterialId == issue.MaterialId, cancellationToken);

        var qualification = "primary";
        if (!isPrimary)
        {
            if (string.IsNullOrWhiteSpace(workOrder.ProductionVersionId) ||
                !await qualificationProvider.IsFrozenSubstituteAsync(
                    new MesMaterialQualificationRequest(
                        request.OrganizationId,
                        request.EnvironmentId,
                        workOrder.SkuId,
                        workOrder.ProductionVersionId,
                        issue.MaterialId),
                    cancellationToken))
            {
                return MesMaterialScanPrevalidationResponse.Reject(
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
            return MesMaterialScanPrevalidationResponse.Reject(
                request, reasonCode, evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        return new MesMaterialScanPrevalidationResponse(
            "accepted", "material-scan-accepted", request.MaterialIssueRequestId, request.WorkOrderId,
            request.OperationTaskId, issue.MaterialId, issue.MaterialLotId, qualification, evaluatedAtUtc);
    }
}

public sealed class HttpMesMaterialPrevalidationProvider(
    MesProductEngineeringHttpClient productEngineeringClient,
    MesInventoryHttpClient inventoryClient,
    IInternalServiceTokenProvider? internalTokenProvider = null)
    : IMesMaterialQualificationProvider, IMesMaterialLotAvailabilityProvider
{
    public async Task<bool> IsFrozenSubstituteAsync(
        MesMaterialQualificationRequest request,
        CancellationToken cancellationToken)
    {
        var versions = await GetAsync<MaterialScanProductionVersionListResponse>(
            productEngineeringClient.HttpClient,
            "ProductEngineering",
            "/api/business/v1/engineering/production-versions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.FinishedSkuId),
                ("skip", 0),
                ("take", 500)),
            cancellationToken);
        var matches = versions.Items.Where(x =>
            string.Equals(x.ProductionVersionId, request.ProductionVersionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.OrganizationId, request.OrganizationId, StringComparison.Ordinal) &&
            string.Equals(x.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal) &&
            string.Equals(x.SkuCode, request.FinishedSkuId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1 || (matches.Length == 0 && versions.Total > versions.Items.Count))
        {
            throw SourceUnavailable("ProductEngineering 冻结生产版本列表不完整或存在重复项。");
        }

        var version = matches.SingleOrDefault();
        if (version is null || !TryParseVersionReference(version.MbomVersionId, out var bomCode, out var revision))
        {
            return false;
        }

        var bom = await GetAsync<ManufacturingBomListItem>(
            productEngineeringClient.HttpClient,
            "ProductEngineering",
            $"/api/business/v1/engineering/manufacturing-boms/{Uri.EscapeDataString(bomCode)}/{Uri.EscapeDataString(revision)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            cancellationToken);
        if (!string.Equals(bom.BomCode, bomCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(bom.Revision, revision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(bom.SkuCode, request.FinishedSkuId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(bom.Status, "published", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return bom.MaterialLines.Any(line => SplitSubstituteCodes(line.SubstituteSkuCodes)
            .Contains(request.MaterialId, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<MesMaterialLotAvailabilityResult> GetAsync(
        MesMaterialLotAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var response = await GetAsync<MaterialScanInventoryAvailabilityResponse>(
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
        CancellationToken cancellationToken) where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(internalTokenProvider?.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalTokenProvider.BearerToken);
        }

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

            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
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

internal sealed record MaterialScanProductionVersionListResponse(
    IReadOnlyCollection<MaterialScanProductionVersionItem> Items,
    int Total);

internal sealed record MaterialScanProductionVersionItem(
    string ProductionVersionId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string MbomVersionId);

internal sealed record MaterialScanInventoryAvailabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string? LocationCode,
    string? LotNo,
    IReadOnlyCollection<MaterialScanInventoryAvailabilityLine> Items);

internal sealed record MaterialScanInventoryAvailabilityLine(
    string LocationCode,
    string? LotNo,
    bool IsExpired,
    bool MovementAllowed,
    decimal OnHandQuantity);
