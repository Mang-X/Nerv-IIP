using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Web.Application.Commands;
using Nerv.IIP.Ops.Web.Endpoints;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Endpoints.OperationTemplates;

public sealed record CreateOperationTemplateCommand(CreateOperationTemplateRequest Request, DateTimeOffset Now)
    : NetCorePal.Extensions.Primitives.ICommand<OperationTemplateResponse>;

public sealed class CreateOperationTemplateCommandHandler(IOperationTemplateApplicationService templates)
    : NetCorePal.Extensions.Primitives.ICommandHandler<CreateOperationTemplateCommand, OperationTemplateResponse>
{
    public async Task<OperationTemplateResponse> Handle(CreateOperationTemplateCommand request, CancellationToken cancellationToken)
    {
        return await templates.CreateAsync(request.Request, request.Now, cancellationToken);
    }
}

public sealed record ListOperationTemplatesQuery : IQuery<OperationTemplateListResponse>;

public sealed class ListOperationTemplatesQueryHandler(IOperationTemplateApplicationService templates)
    : IQueryHandler<ListOperationTemplatesQuery, OperationTemplateListResponse>
{
    public async Task<OperationTemplateListResponse> Handle(ListOperationTemplatesQuery request, CancellationToken cancellationToken)
    {
        return await templates.ListAsync(cancellationToken);
    }
}

public sealed record GetOperationTemplateQuery(string OperationCode) : IQuery<OperationTemplateResponse>;

public sealed class GetOperationTemplateQueryHandler(IOperationTemplateApplicationService templates)
    : IQueryHandler<GetOperationTemplateQuery, OperationTemplateResponse>
{
    public async Task<OperationTemplateResponse> Handle(GetOperationTemplateQuery request, CancellationToken cancellationToken)
    {
        return await templates.GetAsync(request.OperationCode, cancellationToken);
    }
}

[HttpPost("/api/ops/v1/operation-templates")]
[AllowAnonymous]
public sealed class CreateOperationTemplateEndpoint(IMediator mediator)
    : Endpoint<CreateOperationTemplateRequest, ResponseData<OperationTemplateResponse>>
{
    public override async Task HandleAsync(CreateOperationTemplateRequest req, CancellationToken ct)
    {
        try
        {
            var template = await mediator.Send(new CreateOperationTemplateCommand(req, DateTimeOffset.UtcNow), ct);
            await Send.OkAsync(template.AsResponseData(), ct);
        }
        catch (InvalidOperationTaskRequestException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status400BadRequest, ex.Message, ct);
        }
    }
}

[HttpGet("/api/ops/v1/operation-templates")]
[AllowAnonymous]
public sealed class ListOperationTemplatesEndpoint(IMediator mediator)
    : EndpointWithoutRequest<ResponseData<OperationTemplateListResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var templates = await mediator.Send(new ListOperationTemplatesQuery(), ct);
        await Send.OkAsync(templates.AsResponseData(), ct);
    }
}

[HttpGet("/api/ops/v1/operation-templates/{operationCode}")]
[AllowAnonymous]
public sealed class GetOperationTemplateEndpoint(IMediator mediator)
    : EndpointWithoutRequest<ResponseData<OperationTemplateResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var template = await mediator.Send(new GetOperationTemplateQuery(Route<string>("operationCode")!), ct);
            await Send.OkAsync(template.AsResponseData(), ct);
        }
        catch (OperationTaskNotFoundException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status404NotFound, ex.Message, ct);
        }
    }
}
