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

public sealed class AssignInboundOrderCommandValidator : AbstractValidator<AssignInboundOrderCommand>
{
    public AssignInboundOrderCommandValidator() => WarehouseAssignmentValidation.Configure(this);
}

public sealed class AssignPutawayTaskCommandValidator : AbstractValidator<AssignPutawayTaskCommand>
{
    public AssignPutawayTaskCommandValidator() => WarehouseAssignmentValidation.Configure(this);
}

public sealed class AssignOutboundOrderCommandValidator : AbstractValidator<AssignOutboundOrderCommand>
{
    public AssignOutboundOrderCommandValidator() => WarehouseAssignmentValidation.Configure(this);
}

public sealed class AssignPickingTaskCommandValidator : AbstractValidator<AssignPickingTaskCommand>
{
    public AssignPickingTaskCommandValidator() => WarehouseAssignmentValidation.Configure(this);
}

public sealed class AssignCountExecutionCommandValidator : AbstractValidator<AssignCountExecutionCommand>
{
    public AssignCountExecutionCommandValidator() => WarehouseAssignmentValidation.Configure(this);
}

/// <summary>
/// 受控分配家族 5 条命令共享的入参规则入口。
/// </summary>
/// <remarks>
/// <para>姿势与 <c>WarehouseTaskActionValidation.Configure</c> 一致：规则写在一处，
/// 由每条命令**自己的具体校验器**调用。原先这里是
/// <c>sealed class WarehouseAssignmentCommandValidator&lt;TCommand&gt; : AbstractValidator&lt;TCommand&gt;</c>——
/// sealed 开放泛型无法派生闭合，全仓零引用，唯一注册路径
/// <c>AddValidatorsFromAssembly</c> 不注册泛型定义（实测：5 条命令
/// <c>IValidator&lt;C&gt;</c> 在真实 host 里解析数均为 0），
/// 于是这 9 条规则一条都没跑过（#3291）。</para>
/// <para><b>不要改回泛型校验器形态</b>：只要具体校验器消失，程序集扫描就再也看不见这些规则，
/// 而且不会有任何编译错误。<c>WarehouseAssignmentValidatorRegistrationTests</c>
/// 按「实现 <see cref="IWarehouseAssignmentCommand"/> 的每个命令类型都必须能从真实容器解析出校验器」
/// 断言，新增第 6 条分配命令而不给它校验器同样会红。</para>
/// </remarks>
internal static class WarehouseAssignmentValidation
{
    public static void Configure<TCommand>(AbstractValidator<TCommand> validator)
        where TCommand : IWarehouseAssignmentCommand
    {
        validator.RuleFor(command => command.OrganizationId).NotEmpty().MaximumLength(100);
        validator.RuleFor(command => command.EnvironmentId).NotEmpty().MaximumLength(100);
        validator.RuleFor(command => command.AssignerPrincipalId).NotEmpty().MaximumLength(150);
        validator.RuleFor(command => command.AuthorizedSiteCodes).NotEmpty();
        validator.RuleForEach(command => command.AuthorizedSiteCodes).NotEmpty().MaximumLength(100);
        validator.RuleFor(command => command.PoolCode).NotEmpty().MaximumLength(150);
        validator.RuleFor(command => command.OperatorPrincipalId).MaximumLength(150);
        validator.RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(128);
        validator.RuleFor(command => command.ExpectedVersion).GreaterThan(0);
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
