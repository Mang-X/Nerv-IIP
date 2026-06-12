using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Infrastructure.Repositories;
using Nerv.IIP.Ops.Web.Application;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public interface IOperationTemplateApplicationService
{
    Task<OperationTemplateResponse> CreateAsync(CreateOperationTemplateRequest request, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OperationTemplateListResponse> ListAsync(CancellationToken cancellationToken);
    Task<OperationTemplateResponse> GetAsync(string operationCode, CancellationToken cancellationToken);
}

public sealed class InMemoryOperationTemplateApplicationService(IOpsStateStore store) : IOperationTemplateApplicationService
{
    public Task<OperationTemplateResponse> CreateAsync(CreateOperationTemplateRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.CreateTemplate(request.ToDomainInput(), now).ToContract());
    }

    public Task<OperationTemplateListResponse> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(store.ListTemplates().ToContract());
    }

    public Task<OperationTemplateResponse> GetAsync(string operationCode, CancellationToken cancellationToken)
    {
        return Task.FromResult(store.GetTemplate(operationCode).ToContract());
    }
}

public sealed class EfOperationTemplateApplicationService(
    IOperationTemplateRepository repository,
    ApplicationDbContext context) : IOperationTemplateApplicationService
{
    public async Task<OperationTemplateResponse> CreateAsync(CreateOperationTemplateRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var normalized = OperationTemplate.CreateSnapshot(
            request.OperationCode,
            enabled: true,
            request.DefaultMaxAttempts,
            request.DefaultLeaseDurationSeconds,
            request.RequiresApproval);
        var existing = await repository.GetByOperationCodeAsync(normalized.OperationCode, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationTaskRequestException($"Operation template already exists: {normalized.OperationCode}");
        }

        var template = OperationTemplate.Create(
            await repository.NextTemplateIdAsync(cancellationToken),
            normalized.OperationCode,
            request.DisplayName,
            request.ParameterSchemaJson,
            request.RiskLevel,
            normalized.DefaultMaxAttempts,
            normalized.DefaultLeaseDurationSeconds,
            normalized.RequiresApproval,
            now);
        await repository.AddAsync(template, cancellationToken);
        return ToResponse(template);
    }

    public async Task<OperationTemplateListResponse> ListAsync(CancellationToken cancellationToken)
    {
        var items = await context.OperationTemplates
            .AsNoTracking()
            .OrderBy(x => x.OperationCode)
            .Select(x => ToResponse(x))
            .ToListAsync(cancellationToken);

        return new OperationTemplateListResponse(items);
    }

    public async Task<OperationTemplateResponse> GetAsync(string operationCode, CancellationToken cancellationToken)
    {
        var template = await repository.GetByOperationCodeAsync(operationCode, cancellationToken)
            ?? throw new OperationTemplateNotFoundException(operationCode);
        return ToResponse(template);
    }

    private static OperationTemplateResponse ToResponse(OperationTemplate template)
    {
        return new OperationTemplateResponse(
            template.Id.Id,
            template.OperationCode,
            template.DisplayName,
            template.ParameterSchemaJson,
            template.RiskLevel,
            template.DefaultMaxAttempts,
            template.DefaultLeaseDurationSeconds,
            template.RequiresApproval,
            template.Enabled,
            template.CreatedAtUtc,
            template.UpdatedAtUtc);
    }
}
