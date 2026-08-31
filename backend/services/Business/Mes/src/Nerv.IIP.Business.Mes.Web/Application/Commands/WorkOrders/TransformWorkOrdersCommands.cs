using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Behaviors;
using Nerv.IIP.Business.Mes.Web.Application.Errors;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record WorkOrderTransformationTarget(string WorkOrderId, decimal Quantity);

public sealed record WorkOrderTransformationResult(
    WorkOrderTransformationId TransformationId,
    WorkOrderTransformationType Type,
    IReadOnlyCollection<string> SourceWorkOrderIds,
    IReadOnlyCollection<string> TargetWorkOrderIds,
    bool IsIdempotentReplay);

public sealed record SplitWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string SourceWorkOrderId,
    IReadOnlyCollection<WorkOrderTransformationTarget> Targets,
    string Reason,
    string IdempotencyKey,
    string Actor,
    DateTimeOffset OccurredAtUtc) : ICommand<WorkOrderTransformationResult>, IWorkOrderTransformationConcurrencyCommand;

public sealed class SplitWorkOrderCommandValidator : AbstractValidator<SplitWorkOrderCommand>
{
    public SplitWorkOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceWorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Targets).NotNull().Must(x => x.Count >= 2);
        RuleForEach(x => x.Targets).ChildRules(target =>
        {
            target.RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
            target.RuleFor(x => x.Quantity).GreaterThan(0m);
        });
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class SplitWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<SplitWorkOrderCommand, WorkOrderTransformationResult>
{
    public async Task<WorkOrderTransformationResult> Handle(SplitWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var fingerprint = WorkOrderTransformationCommandSupport.SplitFingerprint(request);
        var replay = await WorkOrderTransformationCommandSupport.FindReplayAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, request.IdempotencyKey, fingerprint,
            WorkOrderTransformationType.Split, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var source = await WorkOrderTransformationCommandSupport.GetWorkOrderAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, request.SourceWorkOrderId, cancellationToken);
        await WorkOrderTransformationCommandSupport.EnsureTargetsAreNewAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, request.Targets.Select(x => x.WorkOrderId), cancellationToken);

        var sourceSnapshot = WorkOrderTransformationCommandSupport.Snapshot(source);
        var targets = request.Targets.Select(target => WorkOrderTransformationCommandSupport.CreateTarget(
            source, target.WorkOrderId, target.Quantity)).ToArray();
        var targetSnapshots = targets.Select(WorkOrderTransformationCommandSupport.Snapshot).ToArray();
        WorkOrderTransformation transformation;
        try
        {
            transformation = WorkOrderTransformation.CreateSplit(
                request.OrganizationId, request.EnvironmentId, sourceSnapshot, targetSnapshots, request.IdempotencyKey,
                fingerprint, request.Actor, request.Reason, request.OccurredAtUtc);
        }
        catch (ArgumentException)
        {
            throw new MesLifecycleConflictException("work-order-transformation", "invalid-split");
        }
        catch (InvalidOperationException)
        {
            throw new MesLifecycleConflictException("work-order-transformation", "invalid-split");
        }

        source.MarkSplit();
        dbContext.WorkOrders.AddRange(targets);
        dbContext.WorkOrderTransformations.Add(transformation);
        return new WorkOrderTransformationResult(
            transformation.Id,
            transformation.Type,
            [source.WorkOrderIdValue],
            targets.Select(x => x.WorkOrderIdValue).ToArray(),
            false);
    }
}

public sealed record MergeWorkOrdersCommand(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> SourceWorkOrderIds,
    string TargetWorkOrderId,
    string Reason,
    string IdempotencyKey,
    string Actor,
    DateTimeOffset OccurredAtUtc) : ICommand<WorkOrderTransformationResult>, IWorkOrderTransformationConcurrencyCommand;

public sealed class MergeWorkOrdersCommandValidator : AbstractValidator<MergeWorkOrdersCommand>
{
    public MergeWorkOrdersCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceWorkOrderIds).NotNull().Must(x => x.Count >= 2);
        RuleForEach(x => x.SourceWorkOrderIds).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TargetWorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class MergeWorkOrdersCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<MergeWorkOrdersCommand, WorkOrderTransformationResult>
{
    public async Task<WorkOrderTransformationResult> Handle(MergeWorkOrdersCommand request, CancellationToken cancellationToken)
    {
        var fingerprint = WorkOrderTransformationCommandSupport.MergeFingerprint(request);
        var replay = await WorkOrderTransformationCommandSupport.FindReplayAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, request.IdempotencyKey, fingerprint,
            WorkOrderTransformationType.Merge, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var sourceIds = request.SourceWorkOrderIds.Select(x => x.Trim()).ToArray();
        if (sourceIds.Distinct(StringComparer.Ordinal).Count() != sourceIds.Length)
        {
            throw new KnownException("合并源工单不能包含重复身份。");
        }

        var sources = await dbContext.WorkOrders
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId &&
                sourceIds.Contains(x.WorkOrderIdValue))
            .OrderBy(x => x.WorkOrderIdValue)
            .ToArrayAsync(cancellationToken);
        if (sources.Length != sourceIds.Length)
        {
            throw new KnownException("存在未找到的合并源工单。");
        }

        await WorkOrderTransformationCommandSupport.EnsureTargetsAreNewAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, [request.TargetWorkOrderId], cancellationToken);
        var target = WorkOrderTransformationCommandSupport.CreateTarget(
            sources[0], request.TargetWorkOrderId, sources.Sum(x => x.Quantity));
        WorkOrderTransformation transformation;
        try
        {
            transformation = WorkOrderTransformation.CreateMerge(
                request.OrganizationId,
                request.EnvironmentId,
                sources.Select(WorkOrderTransformationCommandSupport.Snapshot).ToArray(),
                WorkOrderTransformationCommandSupport.Snapshot(target),
                request.IdempotencyKey,
                fingerprint,
                request.Actor,
                request.Reason,
                request.OccurredAtUtc);
        }
        catch (ArgumentException)
        {
            throw new MesLifecycleConflictException("work-order-transformation", "invalid-merge");
        }
        catch (InvalidOperationException)
        {
            throw new MesLifecycleConflictException("work-order-transformation", "invalid-merge");
        }

        foreach (var source in sources)
        {
            source.MarkMerged();
        }
        dbContext.WorkOrders.Add(target);
        dbContext.WorkOrderTransformations.Add(transformation);
        return new WorkOrderTransformationResult(
            transformation.Id,
            transformation.Type,
            sources.Select(x => x.WorkOrderIdValue).ToArray(),
            [target.WorkOrderIdValue],
            false);
    }
}

internal static class WorkOrderTransformationCommandSupport
{
    public static async Task<WorkOrderTransformationResult?> FindReplayAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string idempotencyKey,
        string fingerprint,
        WorkOrderTransformationType type,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.WorkOrderTransformations
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.Type != type || !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new MesIdempotencyConflictException();
        }

        var sources = existing.Lines.Select(x => x.SourceWorkOrderId).Distinct(StringComparer.Ordinal).Order().ToArray();
        var targets = existing.Lines.Select(x => x.TargetWorkOrderId).Distinct(StringComparer.Ordinal).Order().ToArray();
        return new WorkOrderTransformationResult(existing.Id, existing.Type, sources, targets, true);
    }

    public static async Task<WorkOrder> GetWorkOrderAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken) =>
        await dbContext.WorkOrders.SingleOrDefaultAsync(x => x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId && x.WorkOrderIdValue == workOrderId, cancellationToken)
        ?? throw new KnownException($"未找到生产工单，WorkOrderId = {workOrderId}");

    public static async Task EnsureTargetsAreNewAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        IEnumerable<string> targetIds,
        CancellationToken cancellationToken)
    {
        var normalized = targetIds.Select(x => x.Trim()).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new KnownException("目标工单不能包含重复身份。");
        }
        var exists = await dbContext.WorkOrders.AnyAsync(x => x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId && normalized.Contains(x.WorkOrderIdValue), cancellationToken);
        if (exists)
        {
            throw new MesLifecycleConflictException("work-order-transformation", "target-already-exists");
        }
    }

    public static WorkOrder CreateTarget(WorkOrder source, string targetWorkOrderId, decimal quantity) =>
        WorkOrder.Create(
            source.OrganizationId,
            source.EnvironmentId,
            targetWorkOrderId.Trim(),
            source.SkuId,
            source.ProductionVersionId,
            quantity,
            source.Priority,
            source.DueUtc,
            source.UomCode,
            CopySourcePlanReference(source.SourcePlanReference),
            source.OverReceiptTolerancePercent);

    public static WorkOrderTransformationWorkOrderSnapshot Snapshot(WorkOrder workOrder) =>
        new(workOrder.WorkOrderIdValue, workOrder.SkuId, workOrder.ProductionVersionId, workOrder.UomCode,
            workOrder.Quantity, workOrder.Status, workOrder.Version);

    public static string SplitFingerprint(SplitWorkOrderCommand request) => Fingerprint(
        "split",
        request.OrganizationId,
        request.EnvironmentId,
        request.SourceWorkOrderId,
        string.Join(',', request.Targets.OrderBy(x => x.WorkOrderId, StringComparer.Ordinal)
            .Select(x => $"{x.WorkOrderId.Trim()}:{x.Quantity:G29}")),
        request.Reason.Trim(),
        request.Actor.Trim());

    public static string MergeFingerprint(MergeWorkOrdersCommand request) => Fingerprint(
        "merge",
        request.OrganizationId,
        request.EnvironmentId,
        string.Join(',', request.SourceWorkOrderIds.Select(x => x.Trim()).Order(StringComparer.Ordinal)),
        request.TargetWorkOrderId.Trim(),
        request.Reason.Trim(),
        request.Actor.Trim());

    private static SourcePlanReference? CopySourcePlanReference(SourcePlanReference? source) => source is null
        ? null
        : new SourcePlanReference(source.SourceSystem, source.SourceDocumentType, source.SourceDocumentId,
            source.SourceDemandReference, source.SourceDemandReferences);

    private static string Fingerprint(params string[] values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', values))));
}
