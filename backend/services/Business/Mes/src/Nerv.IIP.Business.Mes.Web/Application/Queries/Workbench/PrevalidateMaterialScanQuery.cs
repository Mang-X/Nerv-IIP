using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.ServiceAuth;
using ContractStockAvailabilityResponse = Nerv.IIP.Contracts.Inventory.StockAvailabilityResponse;
using MesMaterialScanPrevalidationResponse = Nerv.IIP.Contracts.Mes.MesMaterialScanPrevalidationResponse;

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

internal static class MaterialScanPrevalidationErrors
{
    internal const string SourceUnavailableCode = "MATERIAL_SCAN_SOURCE_UNAVAILABLE";
    internal const string SourceUnavailableMessage =
        SourceUnavailableCode + ": 物料扫码预校验来源不可用，请稍后重试。";

    internal static KnownException SourceUnavailable() => new(SourceUnavailableMessage);
}

public sealed class PrevalidateMaterialScanQueryHandler(
    ApplicationDbContext dbContext,
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

        var snapshotStatus = workOrder.MaterialRequirementSnapshotStatus;
        var snapshotStateClosed =
            (snapshotStatus is WorkOrder.MaterialRequirementSnapshotCapturedStatus
                or WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus) &&
            workOrder.MaterialRequirementSnapshotEvaluatedAtUtc is not null &&
            !string.IsNullOrWhiteSpace(workOrder.MaterialRequirementSnapshotProductionVersionId);
        if (snapshotStateClosed)
        {
            var automaticRebinds = await dbContext.EngineeringChangeWorkOrderImpacts
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.WorkOrderId == request.WorkOrderId &&
                    x.Status == MesEngineeringChangeImpactStatuses.AutoRebound &&
                    x.WorkOrderStatusAtDetection == WorkOrder.ReleasedStatus)
                .Select(x => new MaterialReadinessGuards.AutomaticRebindEdge(
                    x.WorkOrderId,
                    x.ArchivedProductionVersionId,
                    x.SupersededByProductionVersionId))
                .ToArrayAsync(cancellationToken);
            snapshotStateClosed = MaterialReadinessGuards.IsSnapshotVersionCompatible(
                request.WorkOrderId,
                workOrder.MaterialRequirementSnapshotProductionVersionId,
                workOrder.ProductionVersionId,
                automaticRebinds);
        }
        if (!snapshotStateClosed)
        {
            throw MaterialScanPrevalidationErrors.SourceUnavailable();
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

        MaterialRequirementSnapshotCapture latestSnapshot;
        try
        {
            latestSnapshot = await MaterialRequirementSnapshotReader.LoadLatestByWorkOrderAsync(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                cancellationToken);
        }
        catch (JsonException)
        {
            throw MaterialScanPrevalidationErrors.SourceUnavailable();
        }
        var latestRequirements = latestSnapshot.Requirements;
        if (latestSnapshot.CaptureIdentity is not null &&
            latestSnapshot.CaptureIdentity != workOrder.MaterialRequirementSnapshotEvaluatedAtUtc)
        {
            throw MaterialScanPrevalidationErrors.SourceUnavailable();
        }

        if (snapshotStatus == WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus)
        {
            if (latestRequirements.Length != 0)
            {
                throw MaterialScanPrevalidationErrors.SourceUnavailable();
            }

            return Reject(
                request, "material-not-required", evaluatedAtUtc, issue.MaterialId, issue.MaterialLotId);
        }

        if (latestRequirements.Length == 0)
        {
            throw MaterialScanPrevalidationErrors.SourceUnavailable();
        }

        var requirements = latestRequirements.Where(x =>
            x.OperationTaskId == null || x.OperationTaskId == request.OperationTaskId).ToArray();

        var isPrimary = requirements.Any(x =>
            string.Equals(x.MaterialId, issue.MaterialId, StringComparison.OrdinalIgnoreCase));

        var qualification = "primary";
        if (!isPrimary)
        {
            var isFrozenSubstitute = requirements.Any(x =>
                x.SubstituteMaterialIds.Contains(issue.MaterialId, StringComparer.OrdinalIgnoreCase));
            if (!isFrozenSubstitute)
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

public sealed class HttpMesMaterialLotAvailabilityProvider(
    MesInventoryHttpClient inventoryClient,
    IInternalServiceTokenProvider internalTokenProvider,
    IMesIntegrationEventContextAccessor correlationContextAccessor,
    ILogger<HttpMesMaterialLotAvailabilityProvider> logger)
    : IMesMaterialLotAvailabilityProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MesMaterialLotAvailabilityResult> GetAsync(
        MesMaterialLotAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = correlationContextAccessor.GetContext().CorrelationId;
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
            correlationId,
            request,
            cancellationToken,
            ValidateStockAvailabilityJson);
        if (!string.Equals(response.OrganizationId, request.OrganizationId, StringComparison.Ordinal) ||
            !string.Equals(response.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal) ||
            !string.Equals(response.SkuCode, request.MaterialId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.UomCode, request.UomCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.SiteCode, request.SiteCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.LocationCode, request.LocationCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(response.LotNo, request.MaterialLotId, StringComparison.OrdinalIgnoreCase))
        {
            throw DependencyFailure("response-scope", request, correlationId);
        }

        if (response.Items is null || response.Items.Any(x => x is null ||
            !string.Equals(x.LocationCode, request.LocationCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(x.LotNo, request.MaterialLotId, StringComparison.OrdinalIgnoreCase)))
        {
            throw DependencyFailure("line-scope", request, correlationId);
        }

        if (response.OnHandQuantity < 0m ||
            response.Items.Any(x => x.OnHandQuantity < 0m) ||
            response.OnHandQuantity != response.Items.Sum(x => x.OnHandQuantity) ||
            response.Items.Any(x =>
            {
                var expectedExpired = x.ExpiryDate is not null && x.ExpiryDate.Value < request.AsOfDate;
                return x.IsExpired != expectedExpired || (x.IsExpired && x.MovementAllowed);
            }))
        {
            throw DependencyFailure("inventory-contradiction", request, correlationId);
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
        MesMaterialLotAvailabilityRequest businessContext,
        CancellationToken cancellationToken,
        Action<JsonElement>? validateData = null) where T : class
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
            throw DependencyFailure(
                "transport", businessContext, correlationId, serviceName, exception.GetType().Name);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure(
                "timeout", businessContext, correlationId, serviceName, exception.GetType().Name);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw DependencyFailure(
                    "http-status", businessContext, correlationId, serviceName, statusCode: (int)response.StatusCode);
            }

            ResponseDataEnvelope<T>? envelope;
            try
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(json);
                envelope = JsonSerializer.Deserialize<ResponseDataEnvelope<T>>(json, JsonOptions);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind != JsonValueKind.Null)
                {
                    validateData?.Invoke(data);
                }
            }
            catch (JsonException exception)
            {
                throw DependencyFailure(
                    "malformed-json", businessContext, correlationId, serviceName, exception.GetType().Name);
            }
            catch (NotSupportedException exception)
            {
                throw DependencyFailure(
                    "unsupported-content", businessContext, correlationId, serviceName, exception.GetType().Name);
            }
            catch (InventoryContractViolationException exception)
            {
                throw DependencyFailure(
                    "contract-shape", businessContext, correlationId, serviceName, exception.GetType().Name);
            }
            if (envelope is null || !envelope.Success)
            {
                throw DependencyFailure("failure-envelope", businessContext, correlationId, serviceName);
            }
            if (envelope.Data is null)
            {
                throw DependencyFailure("null-data", businessContext, correlationId, serviceName);
            }

            return envelope.Data;
        }
    }

    private static KnownException SourceUnavailable() => MaterialScanPrevalidationErrors.SourceUnavailable();

    private KnownException DependencyFailure(
        string failureKind,
        MesMaterialLotAvailabilityRequest businessContext,
        string correlationId,
        string serviceName = "Inventory",
        string? exceptionType = null,
        int? statusCode = null)
    {
        logger.LogWarning(
            "{errorCode}: {serviceName} material scan dependency failed; failureKind={failureKind}; materialId={materialId}; materialLotId={materialLotId}; correlationId={correlationId}; exceptionType={exceptionType}; statusCode={statusCode}.",
            MaterialScanPrevalidationErrors.SourceUnavailableCode,
            serviceName,
            failureKind,
            businessContext.MaterialId,
            businessContext.MaterialLotId,
            correlationId,
            exceptionType,
            statusCode);
        return SourceUnavailable();
    }

    private static void ValidateStockAvailabilityJson(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !HasString(data, "organizationId") ||
            !HasString(data, "environmentId") ||
            !HasString(data, "skuCode") ||
            !HasString(data, "uomCode") ||
            !HasString(data, "siteCode") ||
            !HasString(data, "locationCode") ||
            !HasString(data, "lotNo") ||
            !HasNumber(data, "onHandQuantity") ||
            !data.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array ||
            items.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object ||
                !HasString(item, "locationCode") ||
                !HasString(item, "lotNo") ||
                !HasNullableDate(item, "expiryDate") ||
                !HasBoolean(item, "isExpired") ||
                !HasBoolean(item, "movementAllowed") ||
                !HasNumber(item, "onHandQuantity")))
        {
            throw new InventoryContractViolationException();
        }
    }

    private sealed class InventoryContractViolationException : Exception;

    private static bool HasString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());

    private static bool HasNumber(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number;

    private static bool HasBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool HasNullableDate(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        (value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(
                value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));

    private static string Query(params (string Name, object? Value)[] values) => string.Join('&', values.Select(x =>
        $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(Format(x.Value))}"));

    private static string Format(object? value) => value switch
    {
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
