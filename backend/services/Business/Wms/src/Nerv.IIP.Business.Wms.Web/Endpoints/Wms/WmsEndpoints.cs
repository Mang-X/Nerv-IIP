using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Endpoints.Wms;

public abstract class WmsEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureWmsContract(WmsEndpointContract contract, params int[] responseStatusCodes)
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
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by WMS endpoints.");
        }

        Tags("Business WMS");
        Policies(contract.AuthorizationPolicy);
        if (responseStatusCodes.Length > 0)
        {
            Description(builder =>
            {
                foreach (var statusCode in responseStatusCodes)
                {
                    if (statusCode == StatusCodes.Status409Conflict)
                    {
                        builder.Produces<
                            Nerv.IIP.Business.Wms.Web.Application.Errors.WmsLifecycleConflictResponse>(
                            statusCode);
                    }
                    else
                    {
                        builder.Produces(statusCode);
                    }
                }
            });
        }
    }
}

public sealed record CreateInboundOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string InboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SiteCode,
    IReadOnlyCollection<WmsInboundLineInput> Lines);
public sealed record CreateInboundOrderResponse(InboundOrderId InboundOrderId);
public sealed record AssignInboundOrderRequest(
    InboundOrderId InboundOrderId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion);
public sealed record ListInboundOrdersRequest(
    string? OrganizationId,
    string? EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null,
    InboundOrderId? InboundOrderId = null,
    string? LocationCode = null,
    string? LotNo = null,
    string? SiteCode = null);
public sealed record CreatePutawayTaskRequest(
    InboundOrderId InboundOrderId,
    string TaskNo,
    string LineNo,
    string FromLocationCode,
    string ToLocationCode,
    decimal Quantity);
public sealed record ListWarehouseTasksRequest(
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? LocationCode = null,
    string? Keyword = null,
    string? LotNo = null,
    string? SiteCode = null);
public sealed record CreateWarehouseTaskResponse(WarehouseTaskId WarehouseTaskId);
public sealed record AssignPutawayTaskRequest(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion);
public sealed record AssignPickingTaskRequest(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion);
public sealed record RecordWarehouseTaskProgressRequest(WarehouseTaskId WarehouseTaskId, decimal ExecutedQuantity);
public sealed record CompleteWarehouseTaskRequest(WarehouseTaskId WarehouseTaskId);
public sealed record StartWarehouseTaskActionRequest(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId);
public sealed record RecordWarehouseTaskProgressActionRequest(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion,
    decimal ExecutedQuantity,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId);
public sealed record ReportWarehouseTaskExceptionActionRequest(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion,
    string ExceptionCode,
    string Reason,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId);
public sealed record CompleteWarehouseTaskActionRequest(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion,
    decimal ExecutedQuantity,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    string? DifferenceReason = null);
public sealed record CompleteInboundOrderRequest(
    InboundOrderId InboundOrderId,
    string IdempotencyKey,
    IReadOnlyCollection<InboundOrderLineCapture>? Lines = null,
    string? OrganizationId = null,
    string? EnvironmentId = null,
    string? ActorPrincipalId = null,
    IReadOnlyCollection<string>? AuthorizedSiteCodes = null,
    string? ScopeKind = null,
    string? ScopeId = null,
    long ExpectedVersion = 0);
public sealed record CompleteMovementResponse(InventoryMovementRequestId? RequestId, string? InventoryMovementId);
public sealed record RetryInboundInventoryPostingRequest(InboundOrderId InboundOrderId, string IdempotencyKey);
public sealed record CancelInboundOrdersForSourceRequest(string OrganizationId, string EnvironmentId, string SourceDocumentType, string SourceDocumentId, string Reason);
public sealed record CancelInboundOrdersForSourceResponse(int CancelledCount);
public sealed record CreateOutboundOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string OutboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SiteCode,
    IReadOnlyCollection<WmsOutboundLineInput> Lines);
public sealed record CreateOutboundOrderResponse(OutboundOrderId OutboundOrderId);
public sealed record AssignOutboundOrderRequest(
    OutboundOrderId OutboundOrderId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion);
public sealed record ListOutboundOrdersRequest(
    string? OrganizationId,
    string? EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null,
    OutboundOrderId? OutboundOrderId = null,
    string? LocationCode = null,
    string? LotNo = null,
    string? SiteCode = null);
public sealed record ListBackorderOrdersRequest(string OrganizationId, string EnvironmentId, int Skip = 0, int Take = 100, string? Status = null, string? Keyword = null);
public sealed record CloseBackorderOrderRequest(BackorderOrderId BackorderOrderId, string Reason);
public sealed record CreatePickingTaskRequest(
    OutboundOrderId OutboundOrderId,
    string TaskNo,
    string LineNo,
    string FromLocationCode,
    string ToLocationCode,
    decimal Quantity);
public sealed record CompleteOutboundOrderRequest(
    OutboundOrderId OutboundOrderId,
    string PackReviewNo,
    bool Passed,
    string IdempotencyKey,
    string? OrganizationId = null,
    string? EnvironmentId = null,
    string? ActorPrincipalId = null,
    IReadOnlyCollection<string>? AuthorizedSiteCodes = null,
    string? ScopeKind = null,
    string? ScopeId = null,
    long ExpectedVersion = 0);
public sealed record CancelOutboundOrderRequest(OutboundOrderId OutboundOrderId, string Reason);
public sealed record CancelOutboundOrderResponse(OutboundOrderId OutboundOrderId, string Status);
public sealed record RetryOutboundInventoryPostingRequest(OutboundOrderId OutboundOrderId, string IdempotencyKey);
public sealed record CreateCountExecutionRequest(
    string OrganizationId,
    string EnvironmentId,
    string CountNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    decimal ExpectedQuantity);
public sealed record CreateCountExecutionResponse(CountExecutionId CountExecutionId);
public sealed record AssignCountExecutionRequest(
    CountExecutionId CountExecutionId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion);
public sealed record ListCountExecutionsRequest(
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? LocationCode = null,
    string? Keyword = null,
    CountExecutionId? CountExecutionId = null,
    string? SiteCode = null);
public sealed record ListWarehouseOperationalCandidatesRequest(
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    string CandidateDomain,
    string? Keyword = null,
    string? SkuCode = null,
    string? LocationCode = null,
    int Take = 50,
    string? SiteCode = null);
public sealed record CompleteCountExecutionRequest(
    CountExecutionId CountExecutionId,
    decimal CountedQuantity,
    string IdempotencyKey,
    string? OrganizationId = null,
    string? EnvironmentId = null,
    string? ActorPrincipalId = null,
    IReadOnlyCollection<string>? AuthorizedSiteCodes = null,
    string? ScopeKind = null,
    string? ScopeId = null,
    long ExpectedVersion = 0);

public sealed class CompleteInboundOrderRequestValidator : Validator<CompleteInboundOrderRequest>
{
    public CompleteInboundOrderRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorizedSiteCodes).NotEmpty();
        RuleForEach(x => x.AuthorizedSiteCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScopeKind).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class CompleteOutboundOrderRequestValidator : Validator<CompleteOutboundOrderRequest>
{
    public CompleteOutboundOrderRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorizedSiteCodes).NotEmpty();
        RuleForEach(x => x.AuthorizedSiteCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScopeKind).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class CompleteCountExecutionRequestValidator : Validator<CompleteCountExecutionRequest>
{
    public CompleteCountExecutionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorizedSiteCodes).NotEmpty();
        RuleForEach(x => x.AuthorizedSiteCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScopeKind).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class ListReceivingQualityGatesRequestValidator
    : Validator<ListReceivingQualityGatesRequest>
{
    public ListReceivingQualityGatesRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorizedSiteCodes).NotEmpty();
        RuleForEach(x => x.AuthorizedSiteCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScopeKind).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.GateStatus).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.InboundOrderNo).MaximumLength(150);
    }
}

public sealed class ListWarehouseOperationalCandidatesRequestValidator
    : Validator<ListWarehouseOperationalCandidatesRequest>
{
    public ListWarehouseOperationalCandidatesRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorizedSiteCodes).NotEmpty();
        RuleForEach(x => x.AuthorizedSiteCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScopeKind).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CandidateDomain)
            .Must(WarehouseOperationalCandidateDomains.IsSupported);
        RuleFor(x => x.Keyword).MaximumLength(150);
        RuleFor(x => x.SkuCode).MaximumLength(100);
        RuleFor(x => x.LocationCode).MaximumLength(100);
        RuleFor(x => x.Take).InclusiveBetween(1, 100);
        RuleFor(x => x.SiteCode).MaximumLength(100);
    }
}

internal sealed record WmsAuthorizedListScope(
    IReadOnlyCollection<string>? OperatorPrincipalIds,
    IReadOnlyCollection<string>? PoolCodes,
    IReadOnlyCollection<string> SiteCodes,
    string ScopeKind,
    string ScopeId);

internal static class WmsAuthorizedListScopeResolver
{
    public static async Task<WmsAuthorizedListScope> ResolveAsync(
        WarehouseWorkScopeAuthorizer authorizer,
        string? organizationId,
        string? environmentId,
        string actorPrincipalId,
        IReadOnlyCollection<string> authorizedSiteCodes,
        string scopeKind,
        string scopeId,
        string? siteCode,
        CancellationToken cancellationToken)
    {
        var selection = await authorizer.ResolveAsync(
            new WarehouseWorkScopeRequest(
                organizationId ?? string.Empty,
                environmentId ?? string.Empty,
                actorPrincipalId,
                authorizedSiteCodes,
                scopeKind,
                scopeId,
                siteCode),
            cancellationToken);
        return selection.AssignedOperatorUserId is not null
            ? new WmsAuthorizedListScope(
                [selection.AssignedOperatorUserId],
                PoolCodes: null,
                selection.SiteCodes,
                selection.ScopeKind,
                selection.ScopeId)
            : new WmsAuthorizedListScope(
                OperatorPrincipalIds: null,
                selection.PoolCodes,
                selection.SiteCodes,
                selection.ScopeKind,
                selection.ScopeId);
    }
}

public sealed record DispatchWcsTaskRequest(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string DispatcherPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    long ExpectedVersion,
    string AdapterType,
    string ExternalTaskId,
    string PayloadJson,
    string? DeviceId = null);
public sealed record DispatchWcsTaskResponse(WcsTaskId WcsTaskId);
public sealed record CompleteWcsTaskRequest(string OrganizationId, string EnvironmentId, string ExternalTaskId, string CompletionPayloadJson);
public sealed record FailWcsTaskRequest(string OrganizationId, string EnvironmentId, string ExternalTaskId, string FailureCode, string FailureMessage);
public sealed record ListWcsTasksRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ExternalTaskId = null,
    WarehouseTaskId? WarehouseTaskId = null,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    bool? Failed = null,
    string? Keyword = null);
public sealed record ListWcsDispatchCircuitsRequest(string OrganizationId, string EnvironmentId);
public sealed record ResetWcsDispatchCircuitRequest(string OrganizationId, string EnvironmentId, string AdapterType, string DeviceId);
public sealed record ListReceivingQualityGatesRequest(
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    int Skip = 0,
    int Take = 100,
    string? GateStatus = null,
    string? Keyword = null,
    bool IncludeNotRequired = false,
    string? InboundOrderNo = null);
public sealed record ListSupplierReturnRequestsRequest(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100, string? Status = null, string? Keyword = null);
public sealed record WarehouseWorkScopeCatalogRequest(
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes);

public sealed class CreateInboundOrderEndpoint(ISender sender) : WmsEndpoint<CreateInboundOrderRequest, ResponseData<CreateInboundOrderResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreateInboundOrderEndpoint>());
    public override async Task HandleAsync(CreateInboundOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateInboundOrderCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.InboundOrderNo,
            req.SourceDocumentType,
            req.SourceDocumentId,
            req.SiteCode,
            req.Lines), ct);
        await Send.OkAsync(new CreateInboundOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListInboundOrdersEndpoint(
    ISender sender,
    WarehouseWorkScopeAuthorizer authorizer)
    : WmsEndpoint<ListInboundOrdersRequest, ResponseData<ListInboundOrdersResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListInboundOrdersEndpoint>());
    public override async Task HandleAsync(ListInboundOrdersRequest req, CancellationToken ct)
    {
        var scope = await WmsAuthorizedListScopeResolver.ResolveAsync(
            authorizer,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.SiteCode,
            ct);
        var result = await sender.Send(new ListInboundOrdersQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Skip,
            req.Take,
            req.Status,
            req.Keyword,
            req.InboundOrderId,
            req.LocationCode,
            req.LotNo,
            scope.OperatorPrincipalIds,
            scope.PoolCodes,
            scope.SiteCodes), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class AssignInboundOrderEndpoint(ISender sender)
    : WmsEndpoint<AssignInboundOrderRequest, ResponseData<WarehouseAssignmentResult>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<AssignInboundOrderEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);

    public override async Task HandleAsync(AssignInboundOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new AssignInboundOrderCommand(
            req.InboundOrderId,
            req.OrganizationId,
            req.EnvironmentId,
            req.AssignerPrincipalId,
            req.AuthorizedSiteCodes,
            req.PoolCode,
            req.OperatorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreatePutawayTaskEndpoint(ISender sender) : WmsEndpoint<CreatePutawayTaskRequest, ResponseData<CreateWarehouseTaskResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreatePutawayTaskEndpoint>());
    public override async Task HandleAsync(CreatePutawayTaskRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreatePutawayTaskCommand(
            req.InboundOrderId,
            req.TaskNo,
            req.LineNo,
            req.FromLocationCode,
            req.ToLocationCode,
            req.Quantity), ct);
        await Send.OkAsync(new CreateWarehouseTaskResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class AssignPutawayTaskEndpoint(ISender sender)
    : WmsEndpoint<AssignPutawayTaskRequest, ResponseData<WarehouseAssignmentResult>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<AssignPutawayTaskEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);

    public override async Task HandleAsync(AssignPutawayTaskRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new AssignPutawayTaskCommand(
            req.WarehouseTaskId,
            req.OrganizationId,
            req.EnvironmentId,
            req.AssignerPrincipalId,
            req.AuthorizedSiteCodes,
            req.PoolCode,
            req.OperatorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListPutawayTasksEndpoint(
    ISender sender,
    WarehouseWorkScopeAuthorizer authorizer)
    : WmsEndpoint<ListWarehouseTasksRequest, ResponseData<ListWarehouseTasksResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListPutawayTasksEndpoint>());
    public override async Task HandleAsync(ListWarehouseTasksRequest req, CancellationToken ct)
    {
        var scope = await WmsAuthorizedListScopeResolver.ResolveAsync(
            authorizer,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.SiteCode,
            ct);
        var response = await sender.Send(new ListWarehouseTasksQuery(
            req.OrganizationId,
            req.EnvironmentId,
            WarehouseTaskType.Putaway,
            req.Skip,
            req.Take,
            req.Status,
            req.LocationCode,
            req.Keyword,
            req.LotNo,
            scope.OperatorPrincipalIds,
            scope.PoolCodes,
            scope.SiteCodes,
            ActorPrincipalId: req.ActorPrincipalId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteInboundOrderEndpoint(ISender sender) : WmsEndpoint<CompleteInboundOrderRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<CompleteInboundOrderEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);
    public override async Task HandleAsync(CompleteInboundOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CompleteInboundOrderCommand(
            req.InboundOrderId,
            req.IdempotencyKey,
            req.Lines,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.ExpectedVersion), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class RetryInboundInventoryPostingEndpoint(ISender sender) : WmsEndpoint<RetryInboundInventoryPostingRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<RetryInboundInventoryPostingEndpoint>());
    public override async Task HandleAsync(RetryInboundInventoryPostingRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RetryInboundInventoryPostingCommand(req.InboundOrderId, req.IdempotencyKey), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class CancelInboundOrdersForSourceEndpoint(ISender sender) : WmsEndpoint<CancelInboundOrdersForSourceRequest, ResponseData<CancelInboundOrdersForSourceResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CancelInboundOrdersForSourceEndpoint>());

    public override async Task HandleAsync(CancelInboundOrdersForSourceRequest req, CancellationToken ct)
    {
        var cancelledCount = await sender.Send(
            new CancelInboundOrdersForSourceCommand(req.OrganizationId, req.EnvironmentId, req.SourceDocumentType, req.SourceDocumentId, req.Reason),
            ct);
        await Send.OkAsync(new CancelInboundOrdersForSourceResponse(cancelledCount).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateOutboundOrderEndpoint(ISender sender) : WmsEndpoint<CreateOutboundOrderRequest, ResponseData<CreateOutboundOrderResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreateOutboundOrderEndpoint>());
    public override async Task HandleAsync(CreateOutboundOrderRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateOutboundOrderCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.OutboundOrderNo,
            req.SourceDocumentType,
            req.SourceDocumentId,
            req.SiteCode,
            req.Lines), ct);
        await Send.OkAsync(new CreateOutboundOrderResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListOutboundOrdersEndpoint(
    ISender sender,
    WarehouseWorkScopeAuthorizer authorizer)
    : WmsEndpoint<ListOutboundOrdersRequest, ResponseData<ListOutboundOrdersResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListOutboundOrdersEndpoint>());
    public override async Task HandleAsync(ListOutboundOrdersRequest req, CancellationToken ct)
    {
        var scope = await WmsAuthorizedListScopeResolver.ResolveAsync(
            authorizer,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.SiteCode,
            ct);
        var result = await sender.Send(new ListOutboundOrdersQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Skip,
            req.Take,
            req.Status,
            req.Keyword,
            req.OutboundOrderId,
            req.LocationCode,
            req.LotNo,
            scope.OperatorPrincipalIds,
            scope.PoolCodes,
            scope.SiteCodes), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class AssignOutboundOrderEndpoint(ISender sender)
    : WmsEndpoint<AssignOutboundOrderRequest, ResponseData<WarehouseAssignmentResult>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<AssignOutboundOrderEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);

    public override async Task HandleAsync(AssignOutboundOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new AssignOutboundOrderCommand(
            req.OutboundOrderId,
            req.OrganizationId,
            req.EnvironmentId,
            req.AssignerPrincipalId,
            req.AuthorizedSiteCodes,
            req.PoolCode,
            req.OperatorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreatePickingTaskEndpoint(ISender sender) : WmsEndpoint<CreatePickingTaskRequest, ResponseData<CreateWarehouseTaskResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreatePickingTaskEndpoint>());
    public override async Task HandleAsync(CreatePickingTaskRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreatePickingTaskCommand(
            req.OutboundOrderId,
            req.TaskNo,
            req.LineNo,
            req.FromLocationCode,
            req.ToLocationCode,
            req.Quantity), ct);
        await Send.OkAsync(new CreateWarehouseTaskResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class AssignPickingTaskEndpoint(ISender sender)
    : WmsEndpoint<AssignPickingTaskRequest, ResponseData<WarehouseAssignmentResult>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<AssignPickingTaskEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);

    public override async Task HandleAsync(AssignPickingTaskRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new AssignPickingTaskCommand(
            req.WarehouseTaskId,
            req.OrganizationId,
            req.EnvironmentId,
            req.AssignerPrincipalId,
            req.AuthorizedSiteCodes,
            req.PoolCode,
            req.OperatorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListPickingTasksEndpoint(
    ISender sender,
    WarehouseWorkScopeAuthorizer authorizer)
    : WmsEndpoint<ListWarehouseTasksRequest, ResponseData<ListWarehouseTasksResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListPickingTasksEndpoint>());
    public override async Task HandleAsync(ListWarehouseTasksRequest req, CancellationToken ct)
    {
        var scope = await WmsAuthorizedListScopeResolver.ResolveAsync(
            authorizer,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.SiteCode,
            ct);
        var response = await sender.Send(new ListWarehouseTasksQuery(
            req.OrganizationId,
            req.EnvironmentId,
            WarehouseTaskType.Picking,
            req.Skip,
            req.Take,
            req.Status,
            req.LocationCode,
            req.Keyword,
            req.LotNo,
            scope.OperatorPrincipalIds,
            scope.PoolCodes,
            scope.SiteCodes,
            ActorPrincipalId: req.ActorPrincipalId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListReplenishmentTasksEndpoint(
    ISender sender,
    WarehouseWorkScopeAuthorizer authorizer)
    : WmsEndpoint<ListWarehouseTasksRequest, ResponseData<ListWarehouseTasksResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListReplenishmentTasksEndpoint>());
    public override async Task HandleAsync(ListWarehouseTasksRequest req, CancellationToken ct)
    {
        var scope = await WmsAuthorizedListScopeResolver.ResolveAsync(
            authorizer,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.SiteCode,
            ct);
        var response = await sender.Send(new ListWarehouseTasksQuery(
            req.OrganizationId,
            req.EnvironmentId,
            WarehouseTaskType.Replenishment,
            req.Skip,
            req.Take,
            req.Status,
            req.LocationCode,
            req.Keyword,
            req.LotNo,
            scope.OperatorPrincipalIds,
            scope.PoolCodes,
            scope.SiteCodes,
            ActorPrincipalId: req.ActorPrincipalId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordWarehouseTaskProgressEndpoint(ISender sender) : WmsEndpoint<RecordWarehouseTaskProgressRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<RecordWarehouseTaskProgressEndpoint>());
    public override async Task HandleAsync(RecordWarehouseTaskProgressRequest req, CancellationToken ct)
    {
        await sender.Send(new RecordWarehouseTaskProgressCommand(req.WarehouseTaskId, req.ExecutedQuantity), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteWarehouseTaskEndpoint(ISender sender) : WmsEndpoint<CompleteWarehouseTaskRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CompleteWarehouseTaskEndpoint>());
    public override async Task HandleAsync(CompleteWarehouseTaskRequest req, CancellationToken ct)
    {
        await sender.Send(new CompleteWarehouseTaskCommand(req.WarehouseTaskId), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public abstract class WarehouseTaskActionEndpoint<TRequest>(
    ISender sender,
    WarehouseTaskType expectedTaskType)
    : WmsEndpoint<TRequest, ResponseData<WarehouseTaskActionResult>>
    where TRequest : notnull
{
    protected ISender Sender { get; } = sender;

    protected WarehouseTaskType ExpectedTaskType { get; } = expectedTaskType;

    protected WarehouseTaskId ResolveWarehouseTaskId(WarehouseTaskId requestId)
    {
        var routeId = Route<string>("warehouseTaskId");
        return string.IsNullOrWhiteSpace(routeId)
            ? requestId
            : new WarehouseTaskId(Guid.Parse(routeId));
    }

    protected void ConfigureActionContract(WmsEndpointContract contract) =>
        ConfigureWmsContract(
            contract,
            StatusCodes.Status403Forbidden,
            StatusCodes.Status409Conflict,
            StatusCodes.Status422UnprocessableEntity);
}

public sealed class StartPutawayTaskEndpoint(ISender sender)
    : WarehouseTaskActionEndpoint<StartWarehouseTaskActionRequest>(sender, WarehouseTaskType.Putaway)
{
    public override void Configure() => ConfigureActionContract(WmsEndpointContracts.Get<StartPutawayTaskEndpoint>());

    public override async Task HandleAsync(StartWarehouseTaskActionRequest req, CancellationToken ct)
    {
        var result = await Sender.Send(new StartWarehouseTaskCommand(
            ResolveWarehouseTaskId(req.WarehouseTaskId),
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion,
            ExpectedTaskType,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordPutawayTaskProgressEndpoint(ISender sender)
    : WarehouseTaskActionEndpoint<RecordWarehouseTaskProgressActionRequest>(sender, WarehouseTaskType.Putaway)
{
    public override void Configure() => ConfigureActionContract(WmsEndpointContracts.Get<RecordPutawayTaskProgressEndpoint>());

    public override async Task HandleAsync(RecordWarehouseTaskProgressActionRequest req, CancellationToken ct)
    {
        var result = await Sender.Send(new RecordWarehouseTaskProgressActionCommand(
            ResolveWarehouseTaskId(req.WarehouseTaskId),
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion,
            req.ExecutedQuantity,
            ExpectedTaskType,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ReportPutawayTaskExceptionEndpoint(ISender sender)
    : WarehouseTaskActionEndpoint<ReportWarehouseTaskExceptionActionRequest>(sender, WarehouseTaskType.Putaway)
{
    public override void Configure() => ConfigureActionContract(WmsEndpointContracts.Get<ReportPutawayTaskExceptionEndpoint>());

    public override async Task HandleAsync(ReportWarehouseTaskExceptionActionRequest req, CancellationToken ct)
    {
        var result = await Sender.Send(new ReportWarehouseTaskExceptionCommand(
            ResolveWarehouseTaskId(req.WarehouseTaskId),
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion,
            req.ExceptionCode,
            req.Reason,
            ExpectedTaskType,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CompletePutawayTaskEndpoint(ISender sender)
    : WarehouseTaskActionEndpoint<CompleteWarehouseTaskActionRequest>(sender, WarehouseTaskType.Putaway)
{
    public override void Configure() => ConfigureActionContract(WmsEndpointContracts.Get<CompletePutawayTaskEndpoint>());

    public override async Task HandleAsync(CompleteWarehouseTaskActionRequest req, CancellationToken ct)
    {
        var result = await Sender.Send(new CompleteWarehouseTaskActionCommand(
            ResolveWarehouseTaskId(req.WarehouseTaskId),
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion,
            req.ExecutedQuantity,
            req.DifferenceReason,
            ExpectedTaskType,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class StartPickingTaskEndpoint(ISender sender)
    : WarehouseTaskActionEndpoint<StartWarehouseTaskActionRequest>(sender, WarehouseTaskType.Picking)
{
    public override void Configure() => ConfigureActionContract(WmsEndpointContracts.Get<StartPickingTaskEndpoint>());

    public override async Task HandleAsync(StartWarehouseTaskActionRequest req, CancellationToken ct)
    {
        var result = await Sender.Send(new StartWarehouseTaskCommand(
            ResolveWarehouseTaskId(req.WarehouseTaskId),
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion,
            ExpectedTaskType,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordPickingTaskProgressEndpoint(ISender sender)
    : WarehouseTaskActionEndpoint<RecordWarehouseTaskProgressActionRequest>(sender, WarehouseTaskType.Picking)
{
    public override void Configure() => ConfigureActionContract(WmsEndpointContracts.Get<RecordPickingTaskProgressEndpoint>());

    public override async Task HandleAsync(RecordWarehouseTaskProgressActionRequest req, CancellationToken ct)
    {
        var result = await Sender.Send(new RecordWarehouseTaskProgressActionCommand(
            ResolveWarehouseTaskId(req.WarehouseTaskId),
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion,
            req.ExecutedQuantity,
            ExpectedTaskType,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ReportPickingTaskExceptionEndpoint(ISender sender)
    : WarehouseTaskActionEndpoint<ReportWarehouseTaskExceptionActionRequest>(sender, WarehouseTaskType.Picking)
{
    public override void Configure() => ConfigureActionContract(WmsEndpointContracts.Get<ReportPickingTaskExceptionEndpoint>());

    public override async Task HandleAsync(ReportWarehouseTaskExceptionActionRequest req, CancellationToken ct)
    {
        var result = await Sender.Send(new ReportWarehouseTaskExceptionCommand(
            ResolveWarehouseTaskId(req.WarehouseTaskId),
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion,
            req.ExceptionCode,
            req.Reason,
            ExpectedTaskType,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CompletePickingTaskEndpoint(ISender sender)
    : WarehouseTaskActionEndpoint<CompleteWarehouseTaskActionRequest>(sender, WarehouseTaskType.Picking)
{
    public override void Configure() => ConfigureActionContract(WmsEndpointContracts.Get<CompletePickingTaskEndpoint>());

    public override async Task HandleAsync(CompleteWarehouseTaskActionRequest req, CancellationToken ct)
    {
        var result = await Sender.Send(new CompleteWarehouseTaskActionCommand(
            ResolveWarehouseTaskId(req.WarehouseTaskId),
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion,
            req.ExecutedQuantity,
            req.DifferenceReason,
            ExpectedTaskType,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteOutboundOrderEndpoint(ISender sender) : WmsEndpoint<CompleteOutboundOrderRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<CompleteOutboundOrderEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);
    public override async Task HandleAsync(CompleteOutboundOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CompleteOutboundOrderCommand(
            req.OutboundOrderId,
            req.PackReviewNo,
            req.Passed,
            req.IdempotencyKey,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.ExpectedVersion), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class CancelOutboundOrderEndpoint(ISender sender) : WmsEndpoint<CancelOutboundOrderRequest, ResponseData<CancelOutboundOrderResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CancelOutboundOrderEndpoint>());
    public override async Task HandleAsync(CancelOutboundOrderRequest req, CancellationToken ct)
    {
        await sender.Send(new CancelOutboundOrderCommand(req.OutboundOrderId, req.Reason), ct);
        await Send.OkAsync(new CancelOutboundOrderResponse(req.OutboundOrderId, "Cancelled").AsResponseData(), cancellation: ct);
    }
}

public sealed class ListBackorderOrdersEndpoint(ISender sender) : WmsEndpoint<ListBackorderOrdersRequest, ResponseData<ListBackorderOrdersResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListBackorderOrdersEndpoint>());
    public override async Task HandleAsync(ListBackorderOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListBackorderOrdersQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take, req.Status, req.Keyword), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CloseBackorderOrderEndpoint(ISender sender) : WmsEndpoint<CloseBackorderOrderRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CloseBackorderOrderEndpoint>());
    public override async Task HandleAsync(CloseBackorderOrderRequest req, CancellationToken ct)
    {
        await sender.Send(new CloseBackorderOrderCommand(req.BackorderOrderId, req.Reason), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class RetryOutboundInventoryPostingEndpoint(ISender sender) : WmsEndpoint<RetryOutboundInventoryPostingRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<RetryOutboundInventoryPostingEndpoint>());
    public override async Task HandleAsync(RetryOutboundInventoryPostingRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RetryOutboundInventoryPostingCommand(req.OutboundOrderId, req.IdempotencyKey), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateCountExecutionEndpoint(ISender sender) : WmsEndpoint<CreateCountExecutionRequest, ResponseData<CreateCountExecutionResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<CreateCountExecutionEndpoint>());
    public override async Task HandleAsync(CreateCountExecutionRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateCountExecutionCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.CountNo,
            req.SkuCode,
            req.UomCode,
            req.SiteCode,
            req.LocationCode,
            req.ExpectedQuantity), ct);
        await Send.OkAsync(new CreateCountExecutionResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListCountExecutionsEndpoint(
    ISender sender,
    WarehouseWorkScopeAuthorizer authorizer)
    : WmsEndpoint<ListCountExecutionsRequest, ResponseData<ListCountExecutionsResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListCountExecutionsEndpoint>());
    public override async Task HandleAsync(ListCountExecutionsRequest req, CancellationToken ct)
    {
        var scope = await WmsAuthorizedListScopeResolver.ResolveAsync(
            authorizer,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.SiteCode,
            ct);
        var response = await sender.Send(new ListCountExecutionsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Skip,
            req.Take,
            req.Status,
            req.LocationCode,
            req.Keyword,
            req.CountExecutionId,
            scope.OperatorPrincipalIds,
            scope.PoolCodes,
            scope.SiteCodes), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class AssignCountExecutionEndpoint(ISender sender)
    : WmsEndpoint<AssignCountExecutionRequest, ResponseData<WarehouseAssignmentResult>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<AssignCountExecutionEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);

    public override async Task HandleAsync(AssignCountExecutionRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new AssignCountExecutionCommand(
            req.CountExecutionId,
            req.OrganizationId,
            req.EnvironmentId,
            req.AssignerPrincipalId,
            req.AuthorizedSiteCodes,
            req.PoolCode,
            req.OperatorPrincipalId,
            req.IdempotencyKey,
            req.ExpectedVersion), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteCountExecutionEndpoint(ISender sender) : WmsEndpoint<CompleteCountExecutionRequest, ResponseData<CompleteMovementResponse>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<CompleteCountExecutionEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);
    public override async Task HandleAsync(CompleteCountExecutionRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CompleteCountExecutionCommand(
            req.CountExecutionId,
            req.CountedQuantity,
            req.IdempotencyKey,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.ExpectedVersion), ct);
        await Send.OkAsync(new CompleteMovementResponse(result.RequestId, result.InventoryMovementId).AsResponseData(), cancellation: ct);
    }
}

public sealed class DispatchWcsTaskEndpoint(ISender sender) : WmsEndpoint<DispatchWcsTaskRequest, ResponseData<DispatchWcsTaskResponse>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<DispatchWcsTaskEndpoint>(),
        StatusCodes.Status403Forbidden,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);
    public override async Task HandleAsync(DispatchWcsTaskRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new DispatchWcsTaskCommand(
            req.WarehouseTaskId,
            req.OrganizationId,
            req.EnvironmentId,
            req.DispatcherPrincipalId,
            req.AuthorizedSiteCodes,
            req.ExpectedVersion,
            req.AdapterType,
            req.ExternalTaskId,
            req.PayloadJson,
            req.DeviceId), ct);
        await Send.OkAsync(new DispatchWcsTaskResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteWcsTaskEndpoint(ISender sender) : WmsEndpoint<CompleteWcsTaskRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<CompleteWcsTaskEndpoint>(),
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);
    public override async Task HandleAsync(CompleteWcsTaskRequest req, CancellationToken ct)
    {
        await sender.Send(new CompleteWcsTaskCommand(req.OrganizationId, req.EnvironmentId, req.ExternalTaskId, req.CompletionPayloadJson), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class FailWcsTaskEndpoint(ISender sender) : WmsEndpoint<FailWcsTaskRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<FailWcsTaskEndpoint>(),
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity);
    public override async Task HandleAsync(FailWcsTaskRequest req, CancellationToken ct)
    {
        await sender.Send(new FailWcsTaskCommand(req.OrganizationId, req.EnvironmentId, req.ExternalTaskId, req.FailureCode, req.FailureMessage), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListWcsTasksEndpoint(ISender sender) : WmsEndpoint<ListWcsTasksRequest, ResponseData<ListWcsTasksResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListWcsTasksEndpoint>());
    public override async Task HandleAsync(ListWcsTasksRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListWcsTasksQuery(req.OrganizationId, req.EnvironmentId, req.ExternalTaskId, req.WarehouseTaskId, req.Skip, req.Take, req.Status, req.Failed, req.Keyword), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListWcsDispatchCircuitsEndpoint(ISender sender) : WmsEndpoint<ListWcsDispatchCircuitsRequest, ResponseData<IReadOnlyCollection<WcsDispatchCircuitFact>>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListWcsDispatchCircuitsEndpoint>());
    public override async Task HandleAsync(ListWcsDispatchCircuitsRequest req, CancellationToken ct) =>
        await Send.OkAsync((await sender.Send(new ListWcsDispatchCircuitsQuery(req.OrganizationId, req.EnvironmentId), ct)).AsResponseData(), cancellation: ct);
}

public sealed class ResetWcsDispatchCircuitEndpoint(ISender sender) : WmsEndpoint<ResetWcsDispatchCircuitRequest, ResponseData<object>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ResetWcsDispatchCircuitEndpoint>());
    public override async Task HandleAsync(ResetWcsDispatchCircuitRequest req, CancellationToken ct)
    {
        await sender.Send(new ResetWcsDispatchCircuitCommand(req.OrganizationId, req.EnvironmentId, req.AdapterType, req.DeviceId), ct);
        await Send.OkAsync(((object)new { }).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListReceivingQualityGatesEndpoint(
    ISender sender,
    WarehouseWorkScopeAuthorizer authorizer)
    : WmsEndpoint<ListReceivingQualityGatesRequest, ResponseData<ListReceivingQualityGatesResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListReceivingQualityGatesEndpoint>());
    public override async Task HandleAsync(ListReceivingQualityGatesRequest req, CancellationToken ct)
    {
        var scope = await WmsAuthorizedListScopeResolver.ResolveAsync(
            authorizer,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            siteCode: null,
            ct);
        var response = await sender.Send(new ListReceivingQualityGatesQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Skip,
            req.Take,
            req.GateStatus,
            req.Keyword,
            req.IncludeNotRequired,
            req.InboundOrderNo,
            scope.OperatorPrincipalIds,
            scope.PoolCodes,
            scope.SiteCodes), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListSupplierReturnRequestsEndpoint(ISender sender) : WmsEndpoint<ListSupplierReturnRequestsRequest, ResponseData<ListSupplierReturnRequestsResponse>>
{
    public override void Configure() => ConfigureWmsContract(WmsEndpointContracts.Get<ListSupplierReturnRequestsEndpoint>());
    public override async Task HandleAsync(ListSupplierReturnRequestsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListSupplierReturnRequestsQuery(req.OrganizationId, req.EnvironmentId, req.Skip, req.Take, req.Status, req.Keyword), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListWarehouseOperationalCandidatesEndpoint(
    ISender sender,
    WarehouseWorkScopeAuthorizer authorizer)
    : WmsEndpoint<
        ListWarehouseOperationalCandidatesRequest,
        ResponseData<WarehouseOperationalCandidatesResponse>>
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<ListWarehouseOperationalCandidatesEndpoint>(),
        StatusCodes.Status403Forbidden);

    public override async Task HandleAsync(
        ListWarehouseOperationalCandidatesRequest req,
        CancellationToken ct)
    {
        var scope = await WmsAuthorizedListScopeResolver.ResolveAsync(
            authorizer,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedSiteCodes,
            req.ScopeKind,
            req.ScopeId,
            req.SiteCode,
            ct);
        var response = await sender.Send(
            new ListWarehouseOperationalCandidatesQuery(
                req.OrganizationId,
                req.EnvironmentId,
                scope.ScopeKind,
                scope.ScopeId,
                req.CandidateDomain,
                scope.OperatorPrincipalIds,
                scope.PoolCodes,
                scope.SiteCodes,
                req.Keyword,
                req.SkuCode,
                req.LocationCode,
                req.Take),
            ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public abstract class WarehouseWorkScopeCatalogEndpoint(
    ISender sender)
    : WmsEndpoint<WarehouseWorkScopeCatalogRequest, ResponseData<WarehouseWorkScopeCatalog>>
{
    protected async Task SendCatalogAsync(
        WarehouseWorkScopeCatalogRequest request,
        CancellationToken cancellationToken)
    {
        var catalog = await sender.Send(new GetWarehouseWorkScopeCatalogQuery(
            request.OrganizationId,
            request.EnvironmentId,
            request.ActorPrincipalId,
            request.AuthorizedSiteCodes), cancellationToken);
        await Send.OkAsync(catalog.AsResponseData(), cancellation: cancellationToken);
    }
}

public sealed class GetReceiptWorkScopesEndpoint(
    ISender sender)
    : WarehouseWorkScopeCatalogEndpoint(sender)
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<GetReceiptWorkScopesEndpoint>(),
        StatusCodes.Status403Forbidden);

    public override Task HandleAsync(
        WarehouseWorkScopeCatalogRequest req,
        CancellationToken ct) =>
        SendCatalogAsync(req, ct);
}

public sealed class GetShipmentWorkScopesEndpoint(
    ISender sender)
    : WarehouseWorkScopeCatalogEndpoint(sender)
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<GetShipmentWorkScopesEndpoint>(),
        StatusCodes.Status403Forbidden);

    public override Task HandleAsync(
        WarehouseWorkScopeCatalogRequest req,
        CancellationToken ct) =>
        SendCatalogAsync(req, ct);
}

public sealed class GetCountWorkScopesEndpoint(
    ISender sender)
    : WarehouseWorkScopeCatalogEndpoint(sender)
{
    public override void Configure() => ConfigureWmsContract(
        WmsEndpointContracts.Get<GetCountWorkScopesEndpoint>(),
        StatusCodes.Status403Forbidden);

    public override Task HandleAsync(
        WarehouseWorkScopeCatalogRequest req,
        CancellationToken ct) =>
        SendCatalogAsync(req, ct);
}

public sealed record WmsEndpointContract(Type EndpointType, string HttpMethod, string Route, string PermissionCode, string AuthorizationPolicy, string OperationId);

public static class WmsEndpointContracts
{
    public static readonly IReadOnlyCollection<WmsEndpointContract> All =
    [
        new(typeof(CreateInboundOrderEndpoint), "POST", "/api/business/v1/wms/inbound-orders", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "createWmsInboundOrder"),
        new(typeof(ListInboundOrdersEndpoint), "GET", "/api/business/v1/wms/inbound-orders", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsInboundOrders"),
        new(typeof(AssignInboundOrderEndpoint), "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/assignment", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "assignWmsInboundOrder"),
        new(typeof(CreatePutawayTaskEndpoint), "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "createWmsPutawayTask"),
        new(typeof(ListPutawayTasksEndpoint), "GET", "/api/business/v1/wms/putaway-tasks", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsPutawayTasks"),
        new(typeof(AssignPutawayTaskEndpoint), "POST", "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/assignment", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "assignWmsPutawayTask"),
        new(typeof(CompleteInboundOrderEndpoint), "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/complete", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsInboundOrder"),
        new(typeof(RetryInboundInventoryPostingEndpoint), "POST", "/api/business/v1/wms/inbound-orders/{inboundOrderId}/inventory-posting/retry", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "retryWmsInboundInventoryPosting"),
        new(typeof(CancelInboundOrdersForSourceEndpoint), "POST", "/api/business/v1/wms/inbound-orders/cancel-by-source", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "cancelWmsInboundOrdersForSource"),
        new(typeof(CreateOutboundOrderEndpoint), "POST", "/api/business/v1/wms/outbound-orders", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "createWmsOutboundOrder"),
        new(typeof(ListOutboundOrdersEndpoint), "GET", "/api/business/v1/wms/outbound-orders", WmsPermissionCodes.ShipmentsRead, InternalServiceAuthorizationPolicy.Name, "listWmsOutboundOrders"),
        new(typeof(AssignOutboundOrderEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/assignment", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "assignWmsOutboundOrder"),
        new(typeof(CreatePickingTaskEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "createWmsPickingTask"),
        new(typeof(ListPickingTasksEndpoint), "GET", "/api/business/v1/wms/picking-tasks", WmsPermissionCodes.ShipmentsRead, InternalServiceAuthorizationPolicy.Name, "listWmsPickingTasks"),
        new(typeof(AssignPickingTaskEndpoint), "POST", "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/assignment", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "assignWmsPickingTask"),
        new(typeof(ListReplenishmentTasksEndpoint), "GET", "/api/business/v1/wms/replenishment-tasks", WmsPermissionCodes.ShipmentsRead, InternalServiceAuthorizationPolicy.Name, "listWmsReplenishmentTasks"),
        new(typeof(RecordWarehouseTaskProgressEndpoint), "POST", "/api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/progress", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "recordWmsWarehouseTaskProgress"),
        new(typeof(CompleteWarehouseTaskEndpoint), "POST", "/api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/complete", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsWarehouseTask"),
        new(typeof(StartPutawayTaskEndpoint), "POST", "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/start", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "startWmsPutawayTask"),
        new(typeof(RecordPutawayTaskProgressEndpoint), "POST", "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/progress", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "recordWmsPutawayTaskProgress"),
        new(typeof(ReportPutawayTaskExceptionEndpoint), "POST", "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/exception", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "reportWmsPutawayTaskException"),
        new(typeof(CompletePutawayTaskEndpoint), "POST", "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/complete", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsPutawayTask"),
        new(typeof(StartPickingTaskEndpoint), "POST", "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/start", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "startWmsPickingTask"),
        new(typeof(RecordPickingTaskProgressEndpoint), "POST", "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/progress", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "recordWmsPickingTaskProgress"),
        new(typeof(ReportPickingTaskExceptionEndpoint), "POST", "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/exception", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "reportWmsPickingTaskException"),
        new(typeof(CompletePickingTaskEndpoint), "POST", "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/complete", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsPickingTask"),
        new(typeof(CompleteOutboundOrderEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/complete", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsOutboundOrder"),
        new(typeof(CancelOutboundOrderEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/cancel", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "cancelWmsOutboundOrder"),
        new(typeof(ListBackorderOrdersEndpoint), "GET", "/api/business/v1/wms/backorder-orders", WmsPermissionCodes.ShipmentsRead, InternalServiceAuthorizationPolicy.Name, "listWmsBackorderOrders"),
        new(typeof(CloseBackorderOrderEndpoint), "POST", "/api/business/v1/wms/backorder-orders/{backorderOrderId}/close", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "closeWmsBackorderOrder"),
        new(typeof(RetryOutboundInventoryPostingEndpoint), "POST", "/api/business/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry", WmsPermissionCodes.ShipmentsManage, InternalServiceAuthorizationPolicy.Name, "retryWmsOutboundInventoryPosting"),
        new(typeof(CreateCountExecutionEndpoint), "POST", "/api/business/v1/wms/count-executions", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "createWmsCountExecution"),
        new(typeof(ListCountExecutionsEndpoint), "GET", "/api/business/v1/wms/count-executions", WmsPermissionCodes.CountsRead, InternalServiceAuthorizationPolicy.Name, "listWmsCountExecutions"),
        new(typeof(AssignCountExecutionEndpoint), "POST", "/api/business/v1/wms/count-executions/{countExecutionId}/assignment", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "assignWmsCountExecution"),
        new(typeof(CompleteCountExecutionEndpoint), "POST", "/api/business/v1/wms/count-executions/{countExecutionId}/complete", WmsPermissionCodes.ReceiptsManage, InternalServiceAuthorizationPolicy.Name, "completeWmsCountExecution"),
        new(typeof(DispatchWcsTaskEndpoint), "POST", "/api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "dispatchWmsWcsTask"),
        new(typeof(CompleteWcsTaskEndpoint), "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/complete", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "completeWmsWcsTask"),
        new(typeof(FailWcsTaskEndpoint), "POST", "/api/business/v1/wms/wcs-tasks/{externalTaskId}/fail", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "failWmsWcsTask"),
        new(typeof(ListWcsTasksEndpoint), "GET", "/api/business/v1/wms/wcs-tasks", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "listWmsWcsTasks"),
        new(typeof(ListWcsDispatchCircuitsEndpoint), "GET", "/api/business/v1/wms/wcs-dispatch-circuits", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "listWmsWcsDispatchCircuits"),
        new(typeof(ResetWcsDispatchCircuitEndpoint), "POST", "/api/business/v1/wms/wcs-dispatch-circuits/reset", WmsPermissionCodes.AutomationManage, InternalServiceAuthorizationPolicy.Name, "resetWmsWcsDispatchCircuit"),
        new(typeof(ListReceivingQualityGatesEndpoint), "GET", "/api/business/v1/wms/receiving-quality-gates", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsReceivingQualityGates"),
        new(typeof(ListSupplierReturnRequestsEndpoint), "GET", "/api/business/v1/wms/supplier-return-requests", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsSupplierReturnRequests"),
        new(typeof(ListWarehouseOperationalCandidatesEndpoint), "GET", "/api/business/v1/wms/operational-candidates", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "listWmsOperationalCandidates"),
        new(typeof(GetReceiptWorkScopesEndpoint), "GET", "/api/business/v1/wms/work-scopes/receipts", WmsPermissionCodes.ReceiptsRead, InternalServiceAuthorizationPolicy.Name, "getWmsReceiptWorkScopes"),
        new(typeof(GetShipmentWorkScopesEndpoint), "GET", "/api/business/v1/wms/work-scopes/shipments", WmsPermissionCodes.ShipmentsRead, InternalServiceAuthorizationPolicy.Name, "getWmsShipmentWorkScopes"),
        new(typeof(GetCountWorkScopesEndpoint), "GET", "/api/business/v1/wms/work-scopes/counts", WmsPermissionCodes.CountsRead, InternalServiceAuthorizationPolicy.Name, "getWmsCountWorkScopes"),
    ];

    public static WmsEndpointContract Get<TEndpoint>() => All.Single(x => x.EndpointType == typeof(TEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out WmsEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
