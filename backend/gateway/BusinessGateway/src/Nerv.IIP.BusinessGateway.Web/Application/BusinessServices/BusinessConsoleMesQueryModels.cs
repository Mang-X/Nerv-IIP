namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public record BusinessConsoleMesListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkOrderId = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleMesListWithoutStatusRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    int Skip = 0,
    int Take = 100);
