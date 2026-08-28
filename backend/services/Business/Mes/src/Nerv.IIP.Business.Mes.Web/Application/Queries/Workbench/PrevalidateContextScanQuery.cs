using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

public sealed record PrevalidateContextScanQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    string? ScannedOperationTaskId,
    string? DeviceAssetId,
    string? UserId) : IQuery<MesContextScanPrevalidationResponse>;

public sealed class PrevalidateContextScanQueryValidator : AbstractValidator<PrevalidateContextScanQuery>
{
    public PrevalidateContextScanQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationTaskId).NotEmpty().MaximumLength(100);
        RuleFor(x => x).Must(x => new[]
            {
                x.ScannedOperationTaskId,
                x.DeviceAssetId,
                x.UserId,
            }.Count(value => !string.IsNullOrWhiteSpace(value)) == 1)
            .WithMessage("工序、设备和工牌强 ID 必须且只能提供一个。");
        RuleFor(x => x.ScannedOperationTaskId).MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).MaximumLength(100);
        RuleFor(x => x.UserId).MaximumLength(100);
    }
}

public sealed class PrevalidateContextScanQueryHandler(
    ApplicationDbContext dbContext,
    IMesWorkerSkillQualificationGate workerSkillQualificationGate,
    TimeProvider timeProvider)
    : IQueryHandler<PrevalidateContextScanQuery, MesContextScanPrevalidationResponse>
{
    public async Task<MesContextScanPrevalidationResponse> Handle(
        PrevalidateContextScanQuery request,
        CancellationToken cancellationToken)
    {
        var evaluatedAtUtc = timeProvider.GetUtcNow();
        var (objectType, scannedObjectId) = request switch
        {
            { ScannedOperationTaskId: { } id } => (MesContextScanObjectType.OperationTask, id),
            { DeviceAssetId: { } id } => (MesContextScanObjectType.DeviceAsset, id),
            _ => (MesContextScanObjectType.Personnel, request.UserId!),
        };
        var task = await dbContext.OperationTasks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.WorkOrderId == request.WorkOrderId &&
            x.OperationTaskIdValue == request.OperationTaskId,
            cancellationToken);
        if (task is null)
        {
            return Response(
                request,
                MesContextScanDecision.Rejected,
                "mes-context-not-found",
                objectType,
                scannedObjectId,
                evaluatedAtUtc);
        }

        var accepted = objectType switch
        {
            MesContextScanObjectType.OperationTask => string.Equals(
                task.OperationTaskIdValue,
                scannedObjectId,
                StringComparison.Ordinal),
            MesContextScanObjectType.DeviceAsset => string.Equals(
                task.DeviceAssetId,
                scannedObjectId,
                StringComparison.Ordinal),
            MesContextScanObjectType.Personnel => string.Equals(
                task.AssignedUserId,
                scannedObjectId,
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(objectType), objectType, null),
        };
        if (objectType == MesContextScanObjectType.Personnel && accepted)
        {
            await workerSkillQualificationGate.EnsureQualifiedAsync(
                task.OrganizationId,
                task.EnvironmentId,
                scannedObjectId,
                task.RequiredSkillCode,
                cancellationToken);
        }
        var objectName = objectType switch
        {
            MesContextScanObjectType.OperationTask => "operation-task",
            MesContextScanObjectType.DeviceAsset => "device-asset",
            _ => "personnel",
        };
        return Response(
            request,
            accepted ? MesContextScanDecision.Accepted : MesContextScanDecision.Rejected,
            accepted ? $"{objectName}-scan-accepted" : $"{objectName}-mismatch",
            objectType,
            scannedObjectId,
            evaluatedAtUtc);
    }

    private static MesContextScanPrevalidationResponse Response(
        PrevalidateContextScanQuery request,
        MesContextScanDecision decision,
        string reasonCode,
        MesContextScanObjectType objectType,
        string scannedObjectId,
        DateTimeOffset evaluatedAtUtc) =>
        new(
            decision,
            reasonCode,
            request.WorkOrderId,
            request.OperationTaskId,
            objectType,
            scannedObjectId,
            evaluatedAtUtc);
}
