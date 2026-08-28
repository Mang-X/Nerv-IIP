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
    MesContextScanObjectType ObjectType,
    string ScannedObjectId) : IQuery<MesContextScanPrevalidationResponse>;

public sealed class PrevalidateContextScanQueryValidator : AbstractValidator<PrevalidateContextScanQuery>
{
    public PrevalidateContextScanQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationTaskId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ObjectType).IsInEnum();
        RuleFor(x => x.ScannedObjectId).NotEmpty().MaximumLength(100);
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
                request.ObjectType,
                request.ScannedObjectId,
                evaluatedAtUtc);
        }

        var accepted = request.ObjectType switch
        {
            MesContextScanObjectType.OperationTask => string.Equals(
                task.OperationTaskIdValue,
                request.ScannedObjectId,
                StringComparison.Ordinal),
            MesContextScanObjectType.DeviceAsset => string.Equals(
                task.DeviceAssetId,
                request.ScannedObjectId,
                StringComparison.Ordinal),
            MesContextScanObjectType.Personnel => string.Equals(
                task.AssignedUserId,
                request.ScannedObjectId,
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(request.ObjectType), request.ObjectType, null),
        };
        if (request.ObjectType == MesContextScanObjectType.Personnel && accepted)
        {
            await workerSkillQualificationGate.EnsureQualifiedAsync(
                task.OrganizationId,
                task.EnvironmentId,
                request.ScannedObjectId,
                task.RequiredSkillCode,
                cancellationToken);
        }
        var objectName = request.ObjectType switch
        {
            MesContextScanObjectType.OperationTask => "operation-task",
            MesContextScanObjectType.DeviceAsset => "device-asset",
            _ => "personnel",
        };
        return Response(
            request,
            accepted ? MesContextScanDecision.Accepted : MesContextScanDecision.Rejected,
            accepted ? $"{objectName}-scan-accepted" : $"{objectName}-mismatch",
            request.ObjectType,
            request.ScannedObjectId,
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
