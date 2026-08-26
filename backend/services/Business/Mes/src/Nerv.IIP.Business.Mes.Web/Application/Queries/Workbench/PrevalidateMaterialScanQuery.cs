using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.ServiceAuth;
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

        var requirements = await dbContext.MaterialRequirements.AsNoTracking().Where(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.WorkOrderId == request.WorkOrderId &&
            (x.OperationTaskId == null || x.OperationTaskId == request.OperationTaskId))
            .ToArrayAsync(cancellationToken);
        var isPrimary = requirements.Any(x =>
            string.Equals(x.MaterialId, issue.MaterialId, StringComparison.OrdinalIgnoreCase));

        var qualification = "primary";
        if (!isPrimary)
        {
            var isFrozenSubstitute = requirements.Any(x =>
                x.GetSubstituteMaterialIds().Contains(issue.MaterialId, StringComparer.OrdinalIgnoreCase));
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
    IMesIntegrationEventContextAccessor correlationContextAccessor)
    : IMesMaterialLotAvailabilityProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(json);
                envelope = JsonSerializer.Deserialize<ResponseDataEnvelope<T>>(json, JsonOptions);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("data", out var data))
                {
                    validateData?.Invoke(data);
                }
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
                !HasBoolean(item, "isExpired") ||
                !HasBoolean(item, "movementAllowed") ||
                !HasNumber(item, "onHandQuantity")))
        {
            throw SourceUnavailable("Inventory 返回的库存权威事实不完整。");
        }
    }

    private static bool HasString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());

    private static bool HasNumber(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number;

    private static bool HasBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static string Query(params (string Name, object? Value)[] values) => string.Join('&', values.Select(x =>
        $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(Format(x.Value))}"));

    private static string Format(object? value) => value switch
    {
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
