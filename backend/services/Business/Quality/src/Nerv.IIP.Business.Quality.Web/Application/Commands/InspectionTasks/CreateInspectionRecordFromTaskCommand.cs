using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;

public sealed record CreateInspectionRecordFromTaskCommand(
    InspectionTaskId InspectionTaskId,
    string InspectorUserId,
    IReadOnlyCollection<InspectionResultLineCommandInput> ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds) : ICommand<InspectionRecordId>;

public sealed class CreateInspectionRecordFromTaskCommandValidator : AbstractValidator<CreateInspectionRecordFromTaskCommand>
{
    public CreateInspectionRecordFromTaskCommandValidator()
    {
        RuleFor(x => x.InspectionTaskId).NotEmpty();
        RuleFor(x => x.InspectorUserId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ResultLines).NotEmpty();
    }
}

public sealed class CreateInspectionRecordFromTaskCommandHandler(
    IInspectionTaskRepository inspectionTaskRepository,
    IInspectionRecordRepository inspectionRecordRepository,
    IInspectionPlanRepository inspectionPlanRepository)
    : ICommandHandler<CreateInspectionRecordFromTaskCommand, InspectionRecordId>
{
    public async Task<InspectionRecordId> Handle(CreateInspectionRecordFromTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await inspectionTaskRepository.GetAsync(request.InspectionTaskId, cancellationToken)
            ?? throw new KnownException($"Inspection task '{request.InspectionTaskId}' was not found.");
        if (task.Status == InspectionTaskStatuses.Completed && task.InspectionRecordId is not null)
        {
            return task.InspectionRecordId;
        }

        var existing = await inspectionRecordRepository.FindBySourceDocumentAsync(
            task.OrganizationId,
            task.EnvironmentId,
            task.SourceType,
            task.SourceService,
            task.SkuCode,
            task.SourceDocumentId,
            cancellationToken);
        if (existing is not null)
        {
            if (task.Status == InspectionTaskStatuses.Pending)
            {
                task.Start(request.InspectorUserId, DateTimeOffset.UtcNow);
            }

            task.Complete(existing.Id, DateTimeOffset.UtcNow);
            return existing.Id;
        }

        var plan = await inspectionPlanRepository.GetWithCharacteristicsAsync(
                task.OrganizationId,
                task.EnvironmentId,
                task.InspectionPlanId,
                cancellationToken)
            ?? throw new KnownException($"Inspection plan '{task.InspectionPlanId}' was not found.");
        var lines = request.ResultLines.Select(x => new InspectionResultLineInput(
            x.CharacteristicCode,
            x.ObservedValue,
            x.UnitCode,
            x.Result,
            x.DefectReason,
            x.DefectQuantity,
            x.AttachmentFileIds,
            x.MeasuredValue)).ToArray();
        var record = InspectionRecord.CreateFromPlan(
            plan,
            task.SourceType,
            task.SourceService,
            task.SourceDocumentId,
            task.SkuCode,
            task.Quantity,
            task.BatchNo,
            task.SerialNo,
            null,
            lines,
            request.DispositionReason,
            request.DispositionAttachmentFileIds);

        task.Start(request.InspectorUserId, DateTimeOffset.UtcNow);
        task.Complete(record.Id, DateTimeOffset.UtcNow);
        await inspectionRecordRepository.AddAsync(record, cancellationToken);
        return record.Id;
    }
}
