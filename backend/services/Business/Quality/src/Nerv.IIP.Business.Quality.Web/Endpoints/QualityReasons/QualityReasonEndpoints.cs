using System.Diagnostics.CodeAnalysis;
using Nerv.IIP.Business.Quality.Web.Application.Auth;
using Nerv.IIP.Business.Quality.Web.Application.Commands.QualityReasons;
using Nerv.IIP.Business.Quality.Web.Application.Queries.QualityReasons;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.QualityReasons;

public sealed record ListQualityReasonsRequest(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled,
    string? Search,
    string? GroupName,
    int Skip = 0,
    int Take = 100);

public sealed record GetQualityReasonRequest(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode);

public sealed record CreateQualityReasonRequest(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode,
    string ReasonName,
    string GroupName,
    string Severity,
    string? DefaultDisposition);

public sealed record UpdateQualityReasonRequest(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode,
    string ReasonName,
    string GroupName,
    string Severity,
    string? DefaultDisposition);

public sealed record ArchiveQualityReasonRequest(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode);

public sealed class ListQualityReasonsEndpoint(ISender sender)
    : QualityEndpoint<ListQualityReasonsRequest, ResponseData<QualityReasonListResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityReasonEndpointContracts.Get<ListQualityReasonsEndpoint>());
    }

    public override async Task HandleAsync(ListQualityReasonsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListQualityReasonsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Enabled,
            req.Search,
            req.GroupName,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetQualityReasonEndpoint(ISender sender)
    : QualityEndpoint<GetQualityReasonRequest, ResponseData<QualityReasonItem>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityReasonEndpointContracts.Get<GetQualityReasonEndpoint>());
    }

    public override async Task HandleAsync(GetQualityReasonRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetQualityReasonQuery(req.OrganizationId, req.EnvironmentId, req.ReasonCode), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateQualityReasonEndpoint(ISender sender)
    : QualityEndpoint<CreateQualityReasonRequest, ResponseData<QualityReasonItem>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityReasonEndpointContracts.Get<CreateQualityReasonEndpoint>());
    }

    public override async Task HandleAsync(CreateQualityReasonRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new CreateQualityReasonCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.ReasonCode,
            req.ReasonName,
            req.GroupName,
            req.Severity,
            req.DefaultDisposition), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class UpdateQualityReasonEndpoint(ISender sender)
    : QualityEndpoint<UpdateQualityReasonRequest, ResponseData<QualityReasonItem>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityReasonEndpointContracts.Get<UpdateQualityReasonEndpoint>());
    }

    public override async Task HandleAsync(UpdateQualityReasonRequest req, CancellationToken ct)
    {
        var routeCode = Route<string>("reasonCode") ?? req.ReasonCode;
        var response = await sender.Send(new UpdateQualityReasonCommand(
            req.OrganizationId,
            req.EnvironmentId,
            routeCode,
            req.ReasonName,
            req.GroupName,
            req.Severity,
            req.DefaultDisposition), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ArchiveQualityReasonEndpoint(ISender sender)
    : QualityEndpoint<ArchiveQualityReasonRequest, ResponseData<QualityReasonItem>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityReasonEndpointContracts.Get<ArchiveQualityReasonEndpoint>());
    }

    public override async Task HandleAsync(ArchiveQualityReasonRequest req, CancellationToken ct)
    {
        var routeCode = Route<string>("reasonCode") ?? req.ReasonCode;
        var response = await sender.Send(new ArchiveQualityReasonCommand(req.OrganizationId, req.EnvironmentId, routeCode), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public static class QualityReasonEndpointContracts
{
    public static readonly IReadOnlyCollection<QualityEndpointContract> All =
    [
        new(typeof(ListQualityReasonsEndpoint), "GET", "/api/business/v1/quality/reason-codes", BusinessPermissionCodes.QualityNcrRead, "listBusinessQualityReasonCodes"),
        new(typeof(GetQualityReasonEndpoint), "GET", "/api/business/v1/quality/reason-codes/{reasonCode}", BusinessPermissionCodes.QualityNcrRead, "getBusinessQualityReasonCode"),
        new(typeof(CreateQualityReasonEndpoint), "POST", "/api/business/v1/quality/reason-codes", BusinessPermissionCodes.QualityNcrManage, "createBusinessQualityReasonCode"),
        new(typeof(UpdateQualityReasonEndpoint), "PUT", "/api/business/v1/quality/reason-codes/{reasonCode}", BusinessPermissionCodes.QualityNcrManage, "updateBusinessQualityReasonCode"),
        new(typeof(ArchiveQualityReasonEndpoint), "POST", "/api/business/v1/quality/reason-codes/{reasonCode}/archive", BusinessPermissionCodes.QualityNcrManage, "archiveBusinessQualityReasonCode"),
    ];

    public static QualityEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out QualityEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
