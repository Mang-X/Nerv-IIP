using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Auth;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.Queries.NonconformanceReports;
using static Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports.NonconformanceReportEndpointMapping;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

internal static class NonconformanceReportEndpointMapping
{
    public static NonconformanceReportDto ToDto(NonconformanceReportResponse response)
    {
        return new NonconformanceReportDto(
            response.NcrId,
            response.OrganizationId,
            response.EnvironmentId,
            response.NcrCode,
            response.SourceType,
            response.SourceDocumentId,
            response.SkuCode,
            response.DefectQuantity,
            response.DefectReason,
            response.BatchNo,
            response.SerialNo,
            response.Status,
            response.DispositionType,
            response.DispositionApprovalChainId,
            response.ReworkWorkOrderId,
            response.ScrapMovementId,
            response.ReturnDocumentId,
            response.AttachmentFileIds,
            response.CreatedAtUtc,
            response.UpdatedAtUtc);
    }
}

public abstract class QualityEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureQualityContract(QualityEndpointContract contract)
    {
        switch (contract.HttpMethod)
        {
            case "GET":
                Get(contract.Route);
                break;
            case "POST":
                Post(contract.Route);
                break;
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by Quality endpoints.");
        }

        Tags("Business Quality");
        Permissions(contract.PermissionCode);
    }
}

public sealed record CreateNonconformanceReportRequest(
    string OrganizationId,
    string EnvironmentId,
    string SourceType,
    string SourceDocumentId,
    string SkuCode,
    decimal DefectQuantity,
    string DefectReason,
    string? BatchNo,
    string? SerialNo,
    IReadOnlyCollection<string>? AttachmentFileIds);

public sealed record CreateNonconformanceReportResponse(NonconformanceReportId NcrId, string NcrCode);

public sealed record ListNonconformanceReportsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? SourceType,
    string? SkuCode,
    int Take = 100);

public sealed record GetNonconformanceReportRequest(NonconformanceReportId NcrId);

public sealed record SubmitNonconformanceReportDispositionRequest(
    NonconformanceReportId NcrId,
    string DispositionType,
    string? DispositionApprovalChainId,
    IReadOnlyCollection<string>? AttachmentFileIds);

public sealed record CloseNonconformanceReportRequest(
    NonconformanceReportId NcrId,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId);

public sealed record AcceptedResponse(bool Accepted);

public sealed record NonconformanceReportDto(
    NonconformanceReportId NcrId,
    string OrganizationId,
    string EnvironmentId,
    string NcrCode,
    string SourceType,
    string SourceDocumentId,
    string SkuCode,
    decimal DefectQuantity,
    string DefectReason,
    string? BatchNo,
    string? SerialNo,
    string Status,
    string? DispositionType,
    string? DispositionApprovalChainId,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId,
    IReadOnlyCollection<string> AttachmentFileIds,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ListNonconformanceReportsEndpointResponse(IReadOnlyCollection<NonconformanceReportDto> Items);

public sealed class CreateNonconformanceReportEndpoint(ISender sender)
    : QualityEndpoint<CreateNonconformanceReportRequest, ResponseData<CreateNonconformanceReportResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<CreateNonconformanceReportEndpoint>());
    }

    public override async Task HandleAsync(CreateNonconformanceReportRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateNonconformanceReportCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.SourceType,
            req.SourceDocumentId,
            req.SkuCode,
            req.DefectQuantity,
            req.DefectReason,
            req.BatchNo,
            req.SerialNo,
            req.AttachmentFileIds ?? []), ct);
        await Send.OkAsync(new CreateNonconformanceReportResponse(result.NcrId, result.NcrCode).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListNonconformanceReportsEndpoint(ISender sender)
    : QualityEndpoint<ListNonconformanceReportsRequest, ResponseData<ListNonconformanceReportsEndpointResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<ListNonconformanceReportsEndpoint>());
    }

    public override async Task HandleAsync(ListNonconformanceReportsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListNonconformanceReportsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Status,
            req.SourceType,
            req.SkuCode,
            req.Take), ct);
        await Send.OkAsync(new ListNonconformanceReportsEndpointResponse(response.Items.Select(ToDto).ToArray()).AsResponseData(), cancellation: ct);
    }
}

public sealed class GetNonconformanceReportEndpoint(ISender sender)
    : QualityEndpoint<GetNonconformanceReportRequest, ResponseData<NonconformanceReportDto>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<GetNonconformanceReportEndpoint>());
    }

    public override async Task HandleAsync(GetNonconformanceReportRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetNonconformanceReportQuery(req.NcrId), ct);
        await Send.OkAsync(ToDto(response).AsResponseData(), cancellation: ct);
    }
}

public sealed class SubmitNonconformanceReportDispositionEndpoint(ISender sender)
    : QualityEndpoint<SubmitNonconformanceReportDispositionRequest, ResponseData<AcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<SubmitNonconformanceReportDispositionEndpoint>());
    }

    public override async Task HandleAsync(SubmitNonconformanceReportDispositionRequest req, CancellationToken ct)
    {
        await sender.Send(new SubmitNonconformanceReportDispositionCommand(
            req.NcrId,
            req.DispositionType,
            req.DispositionApprovalChainId,
            req.AttachmentFileIds ?? []), ct);
        await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct);
    }
}

public sealed class CloseNonconformanceReportEndpoint(ISender sender)
    : QualityEndpoint<CloseNonconformanceReportRequest, ResponseData<AcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<CloseNonconformanceReportEndpoint>());
    }

    public override async Task HandleAsync(CloseNonconformanceReportRequest req, CancellationToken ct)
    {
        await sender.Send(new CloseNonconformanceReportCommand(
            req.NcrId,
            req.ReworkWorkOrderId,
            req.ScrapMovementId,
            req.ReturnDocumentId), ct);
        await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct);
    }
}

public sealed record QualityEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string OperationId);

public static class QualityEndpointContracts
{
    public static readonly IReadOnlyCollection<QualityEndpointContract> All =
    [
        new(typeof(CreateNonconformanceReportEndpoint), "POST", "/api/business/v1/quality/ncrs", BusinessPermissionCodes.QualityNcrManage, "createBusinessQualityNcr"),
        new(typeof(ListNonconformanceReportsEndpoint), "GET", "/api/business/v1/quality/ncrs", BusinessPermissionCodes.QualityNcrRead, "listBusinessQualityNcrs"),
        new(typeof(GetNonconformanceReportEndpoint), "GET", "/api/business/v1/quality/ncrs/{ncrId}", BusinessPermissionCodes.QualityNcrRead, "getBusinessQualityNcr"),
        new(typeof(SubmitNonconformanceReportDispositionEndpoint), "POST", "/api/business/v1/quality/ncrs/{ncrId}/disposition", BusinessPermissionCodes.QualityNcrManage, "submitBusinessQualityNcrDisposition"),
        new(typeof(CloseNonconformanceReportEndpoint), "POST", "/api/business/v1/quality/ncrs/{ncrId}/close", BusinessPermissionCodes.QualityNcrManage, "closeBusinessQualityNcr"),
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
