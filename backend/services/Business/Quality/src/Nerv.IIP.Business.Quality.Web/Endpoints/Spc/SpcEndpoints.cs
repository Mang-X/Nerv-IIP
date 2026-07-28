using Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.Spc;

public sealed record QuerySpcControlChartRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize = 5,
    int Take = 125);

public sealed record QueryProcessCapabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int Take = 125,
    int SubgroupSize = 5);

public sealed record EvaluateSpcControlChartRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize = 5,
    int Take = 125);

public sealed record ListSpcControlChartsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? CharacteristicCode,
    string? WorkCenterId,
    bool? Locked,
    string? Keyword,
    int Skip = 0,
    int Take = 100);

public sealed record ListSpcControlChartsEndpointResponse(
    IReadOnlyCollection<SpcControlChartCatalogItemResponse> Items,
    int Total,
    int LockedCount);

public sealed class ListSpcControlChartsEndpoint(ISender sender)
    : QualityEndpoint<ListSpcControlChartsRequest, ResponseData<ListSpcControlChartsEndpointResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<ListSpcControlChartsEndpoint>());
    }

    public override async Task HandleAsync(ListSpcControlChartsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListSpcControlChartsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.SkuCode,
            req.CharacteristicCode,
            req.WorkCenterId,
            req.Locked,
            req.Keyword,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(
            new ListSpcControlChartsEndpointResponse(response.Items, response.Total, response.LockedCount).AsResponseData(),
            cancellation: ct);
    }
}

public sealed class QuerySpcControlChartEndpoint(ISender sender)
    : QualityEndpoint<QuerySpcControlChartRequest, ResponseData<SpcControlChartResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<QuerySpcControlChartEndpoint>());
    }

    public override async Task HandleAsync(QuerySpcControlChartRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new QuerySpcControlChartQuery(
            OrganizationId: req.OrganizationId,
            EnvironmentId: req.EnvironmentId,
            SkuCode: req.SkuCode,
            CharacteristicCode: req.CharacteristicCode,
            WorkCenterId: req.WorkCenterId,
            SubgroupSize: req.SubgroupSize,
            Take: req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class QueryProcessCapabilityEndpoint(ISender sender)
    : QualityEndpoint<QueryProcessCapabilityRequest, ResponseData<ProcessCapabilityResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<QueryProcessCapabilityEndpoint>());
    }

    public override async Task HandleAsync(QueryProcessCapabilityRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new QueryProcessCapabilityQuery(
            OrganizationId: req.OrganizationId,
            EnvironmentId: req.EnvironmentId,
            SkuCode: req.SkuCode,
            CharacteristicCode: req.CharacteristicCode,
            WorkCenterId: req.WorkCenterId,
            Take: req.Take,
            SubgroupSize: req.SubgroupSize), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class EvaluateSpcControlChartEndpoint(ISender sender)
    : QualityEndpoint<EvaluateSpcControlChartRequest, ResponseData<SpcEvaluationResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<EvaluateSpcControlChartEndpoint>());
    }

    public override async Task HandleAsync(EvaluateSpcControlChartRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new EvaluateSpcControlChartCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.SkuCode,
            req.CharacteristicCode,
            req.WorkCenterId,
            req.SubgroupSize,
            req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class LockSpcControlChartEndpoint(ISender sender)
    : QualityEndpoint<EvaluateSpcControlChartRequest, ResponseData<SpcControlLimitsResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<LockSpcControlChartEndpoint>());
    }

    public override async Task HandleAsync(EvaluateSpcControlChartRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new LockSpcControlChartCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.SkuCode,
            req.CharacteristicCode,
            req.WorkCenterId,
            req.SubgroupSize,
            req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}
