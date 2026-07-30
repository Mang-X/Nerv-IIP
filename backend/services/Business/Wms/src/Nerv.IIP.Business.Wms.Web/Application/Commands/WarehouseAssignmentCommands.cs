using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseAssignmentReceiptAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Application.Commands;

public sealed record WarehouseAssignmentResult(
    string ResourceCategory,
    string ResourceId,
    string SiteCode,
    string PoolCode,
    string? OperatorPrincipalId,
    string AssignedByPrincipalId,
    long Version);

public interface IWarehouseAssignmentCommand
{
    string OrganizationId { get; }

    string EnvironmentId { get; }

    string AssignerPrincipalId { get; }

    IReadOnlyCollection<string> AuthorizedSiteCodes { get; }

    string PoolCode { get; }

    string? OperatorPrincipalId { get; }

    string IdempotencyKey { get; }

    long ExpectedVersion { get; }

    string ResourceLockKey { get; }
}

public sealed record AssignInboundOrderCommand(
    InboundOrderId InboundOrderId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion)
    : ICommand<WarehouseAssignmentResult>, IWarehouseAssignmentCommand
{
    public string ResourceLockKey => $"inbound:{InboundOrderId}";
}

public sealed record AssignPutawayTaskCommand(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion)
    : ICommand<WarehouseAssignmentResult>, IWarehouseAssignmentCommand
{
    public string ResourceLockKey => $"putaway:{WarehouseTaskId}";
}

public sealed record AssignOutboundOrderCommand(
    OutboundOrderId OutboundOrderId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion)
    : ICommand<WarehouseAssignmentResult>, IWarehouseAssignmentCommand
{
    public string ResourceLockKey => $"outbound:{OutboundOrderId}";
}

public sealed record AssignPickingTaskCommand(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion)
    : ICommand<WarehouseAssignmentResult>, IWarehouseAssignmentCommand
{
    public string ResourceLockKey => $"picking:{WarehouseTaskId}";
}

public sealed record AssignCountExecutionCommand(
    CountExecutionId CountExecutionId,
    string OrganizationId,
    string EnvironmentId,
    string AssignerPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string? OperatorPrincipalId,
    string IdempotencyKey,
    long ExpectedVersion)
    : ICommand<WarehouseAssignmentResult>, IWarehouseAssignmentCommand
{
    public string ResourceLockKey => $"count:{CountExecutionId}";
}

public sealed class WarehouseAssignmentCommandValidator<TCommand>
    : AbstractValidator<TCommand>
    where TCommand : IWarehouseAssignmentCommand
{
    public WarehouseAssignmentCommandValidator()
    {
        RuleFor(command => command.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(command => command.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(command => command.AssignerPrincipalId).NotEmpty().MaximumLength(150);
        RuleFor(command => command.AuthorizedSiteCodes).NotEmpty();
        RuleForEach(command => command.AuthorizedSiteCodes).NotEmpty().MaximumLength(100);
        RuleFor(command => command.PoolCode).NotEmpty().MaximumLength(150);
        RuleFor(command => command.OperatorPrincipalId).MaximumLength(150);
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class WarehouseAssignmentCommandLock<TCommand> : ICommandLock<TCommand>
    where TCommand : IBaseCommand, IWarehouseAssignmentCommand
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        TCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandLockSettings(
            $"business-wms:warehouse-assignment:{command.ResourceLockKey}",
            30));
    }
}

public sealed class AssignInboundOrderCommandHandler(
    ApplicationDbContext dbContext,
    WarehouseWorkScopeAuthorizer authorizer)
    : ICommandHandler<AssignInboundOrderCommand, WarehouseAssignmentResult>
{
    public async Task<WarehouseAssignmentResult> Handle(
        AssignInboundOrderCommand request,
        CancellationToken cancellationToken)
    {
        var inbound = await dbContext.InboundOrders
            .SingleOrDefaultAsync(order => order.Id == request.InboundOrderId, cancellationToken)
            ?? throw new WmsLifecycleConflictException("assign-inbound", "not-found");
        return await WarehouseAssignmentExecution.ExecuteAsync(
            dbContext,
            authorizer,
            request,
            "inbound",
            inbound.Id.ToString(),
            inbound.OrganizationId,
            inbound.EnvironmentId,
            inbound.SiteCode,
            () => inbound.Version,
            () => inbound.AssignWorkPool(
                request.PoolCode,
                request.OperatorPrincipalId,
                request.ExpectedVersion),
            cancellationToken);
    }
}

public sealed class AssignPutawayTaskCommandHandler(
    ApplicationDbContext dbContext,
    WarehouseWorkScopeAuthorizer authorizer)
    : ICommandHandler<AssignPutawayTaskCommand, WarehouseAssignmentResult>
{
    public async Task<WarehouseAssignmentResult> Handle(
        AssignPutawayTaskCommand request,
        CancellationToken cancellationToken)
    {
        var task = await WarehouseAssignmentExecution.LoadTaskAsync(
            dbContext,
            request.WarehouseTaskId,
            WarehouseTaskType.Putaway,
            "assign-putaway",
            cancellationToken);
        return await WarehouseAssignmentExecution.ExecuteAsync(
            dbContext,
            authorizer,
            request,
            "putaway",
            task.Id.ToString(),
            task.OrganizationId,
            task.EnvironmentId,
            task.SiteCode,
            () => task.Version,
            () => task.Assign(
                request.PoolCode,
                request.OperatorPrincipalId,
                request.ExpectedVersion),
            cancellationToken);
    }
}

public sealed class AssignOutboundOrderCommandHandler(
    ApplicationDbContext dbContext,
    WarehouseWorkScopeAuthorizer authorizer)
    : ICommandHandler<AssignOutboundOrderCommand, WarehouseAssignmentResult>
{
    public async Task<WarehouseAssignmentResult> Handle(
        AssignOutboundOrderCommand request,
        CancellationToken cancellationToken)
    {
        var outbound = await dbContext.OutboundOrders
            .SingleOrDefaultAsync(order => order.Id == request.OutboundOrderId, cancellationToken)
            ?? throw new WmsLifecycleConflictException("assign-outbound", "not-found");
        return await WarehouseAssignmentExecution.ExecuteAsync(
            dbContext,
            authorizer,
            request,
            "outbound",
            outbound.Id.ToString(),
            outbound.OrganizationId,
            outbound.EnvironmentId,
            outbound.SiteCode,
            () => outbound.Version,
            () => outbound.AssignWorkPool(
                request.PoolCode,
                request.OperatorPrincipalId,
                request.ExpectedVersion),
            cancellationToken);
    }
}

public sealed class AssignPickingTaskCommandHandler(
    ApplicationDbContext dbContext,
    WarehouseWorkScopeAuthorizer authorizer)
    : ICommandHandler<AssignPickingTaskCommand, WarehouseAssignmentResult>
{
    public async Task<WarehouseAssignmentResult> Handle(
        AssignPickingTaskCommand request,
        CancellationToken cancellationToken)
    {
        var task = await WarehouseAssignmentExecution.LoadTaskAsync(
            dbContext,
            request.WarehouseTaskId,
            WarehouseTaskType.Picking,
            "assign-picking",
            cancellationToken);
        return await WarehouseAssignmentExecution.ExecuteAsync(
            dbContext,
            authorizer,
            request,
            "picking",
            task.Id.ToString(),
            task.OrganizationId,
            task.EnvironmentId,
            task.SiteCode,
            () => task.Version,
            () => task.Assign(
                request.PoolCode,
                request.OperatorPrincipalId,
                request.ExpectedVersion),
            cancellationToken);
    }
}

public sealed class AssignCountExecutionCommandHandler(
    ApplicationDbContext dbContext,
    WarehouseWorkScopeAuthorizer authorizer)
    : ICommandHandler<AssignCountExecutionCommand, WarehouseAssignmentResult>
{
    public async Task<WarehouseAssignmentResult> Handle(
        AssignCountExecutionCommand request,
        CancellationToken cancellationToken)
    {
        var count = await dbContext.CountExecutions
            .SingleOrDefaultAsync(
                execution => execution.Id == request.CountExecutionId,
                cancellationToken)
            ?? throw new WmsLifecycleConflictException("assign-count", "not-found");
        return await WarehouseAssignmentExecution.ExecuteAsync(
            dbContext,
            authorizer,
            request,
            "count",
            count.Id.ToString(),
            count.OrganizationId,
            count.EnvironmentId,
            count.SiteCode,
            () => count.Version,
            () => count.AssignWorkPool(
                request.PoolCode,
                request.OperatorPrincipalId,
                request.ExpectedVersion),
            cancellationToken);
    }
}

internal static class WarehouseAssignmentExecution
{
    public static async Task<WarehouseTask> LoadTaskAsync(
        ApplicationDbContext dbContext,
        WarehouseTaskId taskId,
        WarehouseTaskType expectedType,
        string action,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.WarehouseTasks
            .SingleOrDefaultAsync(candidate => candidate.Id == taskId, cancellationToken)
            ?? throw new WmsLifecycleConflictException(action, "not-found");
        if (task.TaskType != expectedType)
        {
            throw new WmsLifecycleConflictException(action, "task-type-mismatch");
        }

        return task;
    }

    public static async Task<WarehouseAssignmentResult> ExecuteAsync<TCommand>(
        ApplicationDbContext dbContext,
        WarehouseWorkScopeAuthorizer authorizer,
        TCommand command,
        string resourceCategory,
        string resourceId,
        string resourceOrganizationId,
        string resourceEnvironmentId,
        string siteCode,
        Func<long> readVersion,
        Action mutate,
        CancellationToken cancellationToken)
        where TCommand : IWarehouseAssignmentCommand
    {
        EnsureTenant(command, resourceOrganizationId, resourceEnvironmentId);
        var authorization = await authorizer.AuthorizeAssignmentAsync(
            new WarehouseAssignmentAuthorizationRequest(
                resourceOrganizationId,
                resourceEnvironmentId,
                command.AssignerPrincipalId,
                command.AuthorizedSiteCodes,
                siteCode,
                command.PoolCode,
                command.OperatorPrincipalId),
            cancellationToken);
        var normalizedIdempotencyKey = WmsText.IdempotencyKey(command.IdempotencyKey);
        var fingerprint = Fingerprint(command);
        var existingReceipt = await dbContext.WarehouseAssignmentReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                receipt => receipt.OrganizationId == resourceOrganizationId
                    && receipt.EnvironmentId == resourceEnvironmentId
                    && receipt.ResourceCategory == resourceCategory
                    && receipt.ResourceId == resourceId
                    && receipt.IdempotencyKey == normalizedIdempotencyKey,
                cancellationToken);
        if (existingReceipt is not null)
        {
            if (!existingReceipt.MatchesPayload(fingerprint))
            {
                throw new WmsIdempotencyConflictException();
            }

            return FromReceipt(existingReceipt);
        }

        try
        {
            mutate();
        }
        catch (ArgumentException exception)
        {
            throw new WmsUnprocessableException(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new WmsLifecycleConflictException(
                $"assign-{resourceCategory}",
                exception.Message);
        }

        var result = new WarehouseAssignmentResult(
            resourceCategory,
            resourceId,
            siteCode,
            authorization.PoolCode,
            authorization.OperatorPrincipalId,
            authorization.AssignerPrincipalId,
            readVersion());
        dbContext.WarehouseAssignmentReceipts.Add(WarehouseAssignmentReceipt.Create(
            resourceOrganizationId,
            resourceEnvironmentId,
            resourceCategory,
            resourceId,
            normalizedIdempotencyKey,
            fingerprint,
            result.SiteCode,
            result.PoolCode,
            result.OperatorPrincipalId,
            result.AssignedByPrincipalId,
            result.Version));
        return result;
    }

    private static void EnsureTenant(
        IWarehouseAssignmentCommand command,
        string resourceOrganizationId,
        string resourceEnvironmentId)
    {
        if (!string.Equals(
                resourceOrganizationId,
                command.OrganizationId,
                StringComparison.Ordinal)
            || !string.Equals(
                resourceEnvironmentId,
                command.EnvironmentId,
                StringComparison.Ordinal))
        {
            throw WmsAuthorizationException.Forbidden("resource-tenant-mismatch");
        }
    }

    private static string Fingerprint(IWarehouseAssignmentCommand command)
    {
        var payload = JsonSerializer.Serialize(new
        {
            command.AssignerPrincipalId,
            command.PoolCode,
            command.OperatorPrincipalId,
            command.ExpectedVersion,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static WarehouseAssignmentResult FromReceipt(
        WarehouseAssignmentReceipt receipt) =>
        new(
            receipt.ResourceCategory,
            receipt.ResourceId,
            receipt.SiteCode,
            receipt.PoolCode,
            receipt.OperatorPrincipalId,
            receipt.AssignedByPrincipalId,
            receipt.ResultVersion);
}
