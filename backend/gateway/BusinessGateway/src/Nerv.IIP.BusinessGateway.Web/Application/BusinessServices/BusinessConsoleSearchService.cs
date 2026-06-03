using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed class BusinessConsoleSearchService(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IBusinessMesClient mes,
    IBusinessIndustrialTelemetryClient industrialTelemetry,
    IInternalServiceTokenProvider tokenProvider)
{
    public const int DefaultTake = 20;
    public const int MaxTake = 50;

    private static readonly TimeSpan SourceTimeout = TimeSpan.FromMilliseconds(1500);

    private static readonly string[] DefaultTypes =
    [
        SearchObjectTypes.MesWorkOrder,
        SearchObjectTypes.MasterDataSku,
        SearchObjectTypes.InventoryBatch,
        SearchObjectTypes.InventoryLot,
        SearchObjectTypes.EquipmentDevice,
        SearchObjectTypes.EquipmentAlarm,
    ];

    public async Task<BusinessConsoleSearchResponse> SearchAsync(
        string bearerToken,
        string organizationId,
        string environmentId,
        BusinessConsoleSearchRequest request,
        CancellationToken cancellationToken)
    {
        var take = ClampTake(request.Take);
        var query = request.Q.Trim();
        var requestedTypes = NormalizeTypes(request.Types);
        var results = new List<BusinessConsoleSearchResult>();
        var sourceStatuses = new Dictionary<string, BusinessConsoleSearchSourceStatus>(StringComparer.Ordinal);
        var typeStatuses = new Dictionary<string, BusinessConsoleSearchTypeStatus>(StringComparer.Ordinal);

        await AddMasterDataSkuResultsAsync(
            bearerToken,
            organizationId,
            environmentId,
            query,
            take,
            requestedTypes,
            sourceStatuses,
            typeStatuses,
            results,
            cancellationToken);
        await AddMesWorkOrderResultsAsync(
            bearerToken,
            organizationId,
            environmentId,
            query,
            take,
            requestedTypes,
            sourceStatuses,
            typeStatuses,
            results,
            cancellationToken);
        await AddInventoryUnsupportedAsync(requestedTypes, sourceStatuses, typeStatuses);
        await AddEquipmentResultsAsync(
            bearerToken,
            organizationId,
            environmentId,
            query,
            take,
            requestedTypes,
            sourceStatuses,
            typeStatuses,
            results,
            cancellationToken);
        AddUnknownUnsupportedTypes(requestedTypes, typeStatuses);

        var orderedResults = results
            .OrderBy(result => result.ObjectType, StringComparer.Ordinal)
            .ThenBy(result => result.ObjectNumber, StringComparer.Ordinal)
            .Take(take)
            .ToArray();

        return new BusinessConsoleSearchResponse(
            query,
            take,
            orderedResults,
            sourceStatuses.Values.OrderBy(status => status.Source, StringComparer.Ordinal).ToArray(),
            typeStatuses.Values.OrderBy(status => status.ObjectType, StringComparer.Ordinal).ToArray());
    }

    private async Task AddMasterDataSkuResultsAsync(
        string bearerToken,
        string organizationId,
        string environmentId,
        string query,
        int take,
        IReadOnlyCollection<string> requestedTypes,
        Dictionary<string, BusinessConsoleSearchSourceStatus> sourceStatuses,
        Dictionary<string, BusinessConsoleSearchTypeStatus> typeStatuses,
        List<BusinessConsoleSearchResult> results,
        CancellationToken cancellationToken)
    {
        if (!requestedTypes.Contains(SearchObjectTypes.MasterDataSku, StringComparer.Ordinal))
        {
            return;
        }

        var authorization = await CheckSourceAsync(
            bearerToken,
            BusinessGatewayPermissions.MasterDataProductsRead,
            organizationId,
            environmentId,
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            SetForbidden(sourceStatuses, typeStatuses, SourceNames.MasterData, SearchObjectTypes.MasterDataSku, BusinessGatewayPermissions.MasterDataProductsRead);
            return;
        }

        try
        {
            var response = await WithSourceTimeoutAsync(
                token => masterData.ListResourcesAsync(
                    tokenProvider.BearerToken,
                    new BusinessConsoleListResourcesRequest(organizationId, environmentId, "sku", false, take),
                    token),
                cancellationToken);
            results.AddRange(response.Resources
                .Where(resource => Matches(query, resource.Code, resource.DisplayName))
                .Take(take)
                .Select(resource => new BusinessConsoleSearchResult(
                    SearchObjectTypes.MasterDataSku,
                    resource.DisplayName,
                    resource.Code,
                    $"/master-data/skus?skuCode={Uri.EscapeDataString(resource.Code)}",
                    resource.Code,
                    resource.Active ? "Active SKU" : "Disabled SKU")));
            SetAvailable(sourceStatuses, typeStatuses, SourceNames.MasterData, SearchObjectTypes.MasterDataSku, BusinessGatewayPermissions.MasterDataProductsRead);
        }
        catch (Exception ex) when (IsProtectedSourceFailure(ex, cancellationToken))
        {
            SetUnavailable(sourceStatuses, typeStatuses, SourceNames.MasterData, SearchObjectTypes.MasterDataSku, BusinessGatewayPermissions.MasterDataProductsRead, FailureReason(ex, cancellationToken));
        }
    }

    private async Task AddMesWorkOrderResultsAsync(
        string bearerToken,
        string organizationId,
        string environmentId,
        string query,
        int take,
        IReadOnlyCollection<string> requestedTypes,
        Dictionary<string, BusinessConsoleSearchSourceStatus> sourceStatuses,
        Dictionary<string, BusinessConsoleSearchTypeStatus> typeStatuses,
        List<BusinessConsoleSearchResult> results,
        CancellationToken cancellationToken)
    {
        if (!requestedTypes.Contains(SearchObjectTypes.MesWorkOrder, StringComparer.Ordinal))
        {
            return;
        }

        var authorization = await CheckSourceAsync(
            bearerToken,
            BusinessGatewayPermissions.MesWorkOrdersRead,
            organizationId,
            environmentId,
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            SetForbidden(sourceStatuses, typeStatuses, SourceNames.BusinessMes, SearchObjectTypes.MesWorkOrder, BusinessGatewayPermissions.MesWorkOrdersRead);
            return;
        }

        try
        {
            var response = await WithSourceTimeoutAsync(
                token => mes.ListWorkOrdersAsync(
                    tokenProvider.BearerToken,
                    new BusinessConsoleMesListRequest(organizationId, environmentId, null, take),
                    token),
                cancellationToken);
            results.AddRange(response.Items
                .Where(item => Matches(query, item.WorkOrderId, item.SkuId, item.ProductionVersionId, item.Status))
                .Take(take)
                .Select(item => new BusinessConsoleSearchResult(
                    SearchObjectTypes.MesWorkOrder,
                    $"Work order {item.WorkOrderId}",
                    item.WorkOrderId,
                    $"/mes/work-orders/{Uri.EscapeDataString(item.WorkOrderId)}",
                    item.WorkOrderId,
                    $"{item.Status} · {item.SkuId} · qty {item.Quantity}")));
            SetAvailable(sourceStatuses, typeStatuses, SourceNames.BusinessMes, SearchObjectTypes.MesWorkOrder, BusinessGatewayPermissions.MesWorkOrdersRead);
        }
        catch (Exception ex) when (IsProtectedSourceFailure(ex, cancellationToken))
        {
            SetUnavailable(sourceStatuses, typeStatuses, SourceNames.BusinessMes, SearchObjectTypes.MesWorkOrder, BusinessGatewayPermissions.MesWorkOrdersRead, FailureReason(ex, cancellationToken));
        }
    }

    private static Task AddInventoryUnsupportedAsync(
        IReadOnlyCollection<string> requestedTypes,
        Dictionary<string, BusinessConsoleSearchSourceStatus> sourceStatuses,
        Dictionary<string, BusinessConsoleSearchTypeStatus> typeStatuses)
    {
        foreach (var objectType in requestedTypes.Where(type =>
                     type is SearchObjectTypes.InventoryBatch or SearchObjectTypes.InventoryLot))
        {
            sourceStatuses[SourceNames.BusinessInventory] = new(
                SourceNames.BusinessInventory,
                "unsupported",
                BusinessGatewayPermissions.InventoryLedgerRead,
                "global-inventory-batch-lot-search-not-connected");
            typeStatuses[objectType] = new(
                objectType,
                SourceNames.BusinessInventory,
                "unsupported",
                BusinessGatewayPermissions.InventoryLedgerRead,
                "global-inventory-batch-lot-search-not-connected");
        }

        return Task.CompletedTask;
    }

    private async Task AddEquipmentResultsAsync(
        string bearerToken,
        string organizationId,
        string environmentId,
        string query,
        int take,
        IReadOnlyCollection<string> requestedTypes,
        Dictionary<string, BusinessConsoleSearchSourceStatus> sourceStatuses,
        Dictionary<string, BusinessConsoleSearchTypeStatus> typeStatuses,
        List<BusinessConsoleSearchResult> results,
        CancellationToken cancellationToken)
    {
        if (requestedTypes.Contains(SearchObjectTypes.EquipmentDevice, StringComparer.Ordinal))
        {
            sourceStatuses[SourceNames.IndustrialTelemetry] = new(
                SourceNames.IndustrialTelemetry,
                "unsupported",
                BusinessGatewayPermissions.IiotTelemetryRead,
                "global-equipment-device-search-not-connected");
            typeStatuses[SearchObjectTypes.EquipmentDevice] = new(
                SearchObjectTypes.EquipmentDevice,
                SourceNames.IndustrialTelemetry,
                "unsupported",
                BusinessGatewayPermissions.IiotTelemetryRead,
                "global-equipment-device-search-not-connected");
        }

        if (!requestedTypes.Contains(SearchObjectTypes.EquipmentAlarm, StringComparer.Ordinal))
        {
            return;
        }

        var authorization = await CheckSourceAsync(
            bearerToken,
            BusinessGatewayPermissions.IiotAlarmsRead,
            organizationId,
            environmentId,
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            SetForbidden(sourceStatuses, typeStatuses, SourceNames.IndustrialTelemetry, SearchObjectTypes.EquipmentAlarm, BusinessGatewayPermissions.IiotAlarmsRead);
            return;
        }

        try
        {
            var response = await WithSourceTimeoutAsync(
                token => industrialTelemetry.ListActiveAlarmsAsync(
                    tokenProvider.BearerToken,
                    new BusinessConsoleEquipmentContextRequest(organizationId, environmentId),
                    token),
                cancellationToken);
            results.AddRange(response.Items
                .Where(item => Matches(query, item.AlarmEventId, item.DeviceAssetId, item.AlarmCode, item.Severity, item.ExternalAlarmId))
                .Take(take)
                .Select(item => new BusinessConsoleSearchResult(
                    SearchObjectTypes.EquipmentAlarm,
                    $"{item.DeviceAssetId} {item.AlarmCode}",
                    item.AlarmEventId,
                    $"/equipment/alarms?alarmEventId={Uri.EscapeDataString(item.AlarmEventId)}",
                    item.AlarmEventId,
                    $"{item.Severity} · {item.DeviceAssetId} · {item.RaisedAtUtc:O}")));
            SetAvailable(sourceStatuses, typeStatuses, SourceNames.IndustrialTelemetry, SearchObjectTypes.EquipmentAlarm, BusinessGatewayPermissions.IiotAlarmsRead);
        }
        catch (Exception ex) when (IsProtectedSourceFailure(ex, cancellationToken))
        {
            SetUnavailable(sourceStatuses, typeStatuses, SourceNames.IndustrialTelemetry, SearchObjectTypes.EquipmentAlarm, BusinessGatewayPermissions.IiotAlarmsRead, FailureReason(ex, cancellationToken));
        }
    }

    private void AddUnknownUnsupportedTypes(
        IReadOnlyCollection<string> requestedTypes,
        Dictionary<string, BusinessConsoleSearchTypeStatus> typeStatuses)
    {
        foreach (var objectType in requestedTypes.Where(type => !typeStatuses.ContainsKey(type)))
        {
            if (objectType is SearchObjectTypes.MasterDataSku or SearchObjectTypes.MesWorkOrder or SearchObjectTypes.EquipmentAlarm)
            {
                continue;
            }

            typeStatuses[objectType] = new(objectType, "Unknown", "unsupported", null, "unsupported-search-type");
        }
    }

    private Task<BusinessGatewayAuthorizationResult> CheckSourceAsync(
        string bearerToken,
        string permissionCode,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        auth.CheckAsync(
            bearerToken,
            new BusinessGatewayPermissionRequirement(permissionCode, organizationId, environmentId, null, null),
            cancellationToken);

    private static async Task<T> WithSourceTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var sourceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sourceCts.CancelAfter(SourceTimeout);
        return await action(sourceCts.Token).WaitAsync(SourceTimeout, cancellationToken);
    }

    private static string[] NormalizeTypes(string? types)
    {
        if (string.IsNullOrWhiteSpace(types))
        {
            return DefaultTypes;
        }

        return types
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeType)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeType(string type) =>
        type.Trim().ToLowerInvariant() switch
        {
            "mesworkorder" or "workorder" or "work-order" or "mes-work-order" => SearchObjectTypes.MesWorkOrder,
            "masterdatasku" or "mastersku" or "sku" or "product" => SearchObjectTypes.MasterDataSku,
            "inventorybatch" or "batch" or "batchno" => SearchObjectTypes.InventoryBatch,
            "inventorylot" or "lot" or "lotno" or "materiallot" => SearchObjectTypes.InventoryLot,
            "equipmentdevice" or "device" or "deviceasset" or "asset" => SearchObjectTypes.EquipmentDevice,
            "equipmentalarm" or "alarm" => SearchObjectTypes.EquipmentAlarm,
            _ => type.Trim(),
        };

    private static int ClampTake(int take) => take switch
    {
        <= 0 => DefaultTake,
        > MaxTake => MaxTake,
        _ => take,
    };

    private static bool Matches(string query, params string?[] values) =>
        values.Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsProtectedSourceFailure(Exception ex, CancellationToken requestCancellationToken) =>
        ex is (BusinessServiceProxyException
            or HttpRequestException
            or InvalidOperationException
            or TimeoutException)
            || ex is TaskCanceledException && !requestCancellationToken.IsCancellationRequested;

    private static string FailureReason(Exception ex, CancellationToken requestCancellationToken) =>
        ex is TimeoutException || ex is TaskCanceledException && !requestCancellationToken.IsCancellationRequested
            ? "source-timeout"
            : "source-unavailable";

    private static void SetAvailable(
        Dictionary<string, BusinessConsoleSearchSourceStatus> sourceStatuses,
        Dictionary<string, BusinessConsoleSearchTypeStatus> typeStatuses,
        string source,
        string objectType,
        string permissionCode)
    {
        sourceStatuses[source] = new(source, "available", permissionCode, null);
        typeStatuses[objectType] = new(objectType, source, "available", permissionCode, null);
    }

    private static void SetForbidden(
        Dictionary<string, BusinessConsoleSearchSourceStatus> sourceStatuses,
        Dictionary<string, BusinessConsoleSearchTypeStatus> typeStatuses,
        string source,
        string objectType,
        string permissionCode)
    {
        sourceStatuses[source] = new(source, "forbidden", permissionCode, "permission-denied");
        typeStatuses[objectType] = new(objectType, source, "forbidden", permissionCode, "permission-denied");
    }

    private static void SetUnavailable(
        Dictionary<string, BusinessConsoleSearchSourceStatus> sourceStatuses,
        Dictionary<string, BusinessConsoleSearchTypeStatus> typeStatuses,
        string source,
        string objectType,
        string permissionCode,
        string reason)
    {
        sourceStatuses[source] = new(source, "unavailable", permissionCode, reason);
        typeStatuses[objectType] = new(objectType, source, "unavailable", permissionCode, reason);
    }

    private static class SourceNames
    {
        public const string MasterData = "BusinessMasterData";
        public const string BusinessMes = "BusinessMES";
        public const string BusinessInventory = "BusinessInventory";
        public const string IndustrialTelemetry = "IndustrialTelemetry";
    }

    private static class SearchObjectTypes
    {
        public const string MesWorkOrder = "mesWorkOrder";
        public const string MasterDataSku = "masterDataSku";
        public const string InventoryBatch = "inventoryBatch";
        public const string InventoryLot = "inventoryLot";
        public const string EquipmentDevice = "equipmentDevice";
        public const string EquipmentAlarm = "equipmentAlarm";
    }
}
