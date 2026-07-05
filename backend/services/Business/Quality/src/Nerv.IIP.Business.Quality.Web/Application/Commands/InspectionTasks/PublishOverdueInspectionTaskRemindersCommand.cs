using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Contracts.Quality;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;

public sealed record PublishOverdueInspectionTaskRemindersCommand(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset NowUtc) : ICommand<int>;

public sealed class PublishOverdueInspectionTaskRemindersCommandValidator : AbstractValidator<PublishOverdueInspectionTaskRemindersCommand>
{
    public PublishOverdueInspectionTaskRemindersCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class PublishOverdueInspectionTaskRemindersCommandHandler(
    ApplicationDbContext dbContext,
    IIntegrationEventPublisher integrationEventPublisher)
    : ICommandHandler<PublishOverdueInspectionTaskRemindersCommand, int>
{
    public async Task<int> Handle(PublishOverdueInspectionTaskRemindersCommand request, CancellationToken cancellationToken)
    {
        var overdueTasks = await dbContext.InspectionTasks
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.Status != InspectionTaskStatuses.Completed &&
                x.DueAtUtc <= request.NowUtc &&
                x.OverdueReminderSentAtUtc == null)
            .OrderBy(x => x.DueAtUtc)
            .Take(100)
            .ToArrayAsync(cancellationToken);

        foreach (var task in overdueTasks)
        {
            var id = task.Id.ToString();
            await integrationEventPublisher.PublishAsync(
                new InspectionTaskOverdueIntegrationEvent(
                    $"evt-{Guid.CreateVersion7():N}",
                    QualityIntegrationEventTypes.InspectionTaskOverdue,
                    QualityIntegrationEventVersions.V1,
                    request.NowUtc,
                    QualityIntegrationEventSources.BusinessQuality,
                    $"quality:inspection-task-overdue:{task.OrganizationId}:{task.EnvironmentId}:{id}",
                    id,
                    task.OrganizationId,
                    task.EnvironmentId,
                    "system:quality",
                    $"quality:inspection-task-overdue:{task.OrganizationId}:{task.EnvironmentId}:{id}",
                    new InspectionTaskOverduePayload(
                        id,
                        task.SourceType,
                        task.SourceService,
                        task.SourceDocumentId,
                        task.SourceDocumentLineId,
                        task.SkuCode,
                        task.DueAtUtc,
                        request.NowUtc)),
                cancellationToken);
            task.MarkOverdueReminderSent(request.NowUtc);
        }

        return overdueTasks.Length;
    }
}
