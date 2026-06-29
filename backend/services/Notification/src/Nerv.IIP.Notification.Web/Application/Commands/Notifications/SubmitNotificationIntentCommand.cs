using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.Repositories;
using Nerv.IIP.Notification.Web.Application.Notifications;
using NetCorePal.Extensions.Primitives;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Messaging.CAP;
using System.Diagnostics;

namespace Nerv.IIP.Notification.Web.Application.Commands.Notifications;

public sealed record SubmitNotificationIntentCommand(
    string OrganizationId,
    string EnvironmentId,
    SubmitNotificationIntentRequest Request,
    DateTimeOffset Now) : ICommand<NotificationIntentResponse>;

public sealed class SubmitNotificationIntentCommandHandler(
    INotificationIntentRepository repository,
    ApplicationDbContext dbContext)
    : ICommandHandler<SubmitNotificationIntentCommand, NotificationIntentResponse>
{
    public async Task<NotificationIntentResponse> Handle(SubmitNotificationIntentCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var existing = await repository.GetByDedupeKeyAsync(
            command.OrganizationId,
            command.EnvironmentId,
            request.SourceService,
            request.SourceEventType,
            request.DedupeKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing.ToResponse(duplicate: true);
        }

        var intent = new NotificationIntent(
            command.OrganizationId,
            command.EnvironmentId,
            request.SourceService,
            request.SourceEventType,
            request.SourceEventId,
            request.IntentType,
            request.Severity,
            request.DedupeKey,
            request.Resource?.ResourceType,
            request.Resource?.ResourceId,
            request.Resource?.FileId,
            request.Title,
            request.Summary,
            request.SuggestedRecipientRefs,
            command.Now);

        await repository.AddAsync(intent, cancellationToken);
        foreach (var message in intent.Messages)
        {
            dbContext.DeliveryAttempts.Add(DeliveryAttempt.Succeeded(
                message.Id,
                NotificationDeliveryChannels.InApp,
                command.Now));
        }

        const string duplicateRecoverySavepoint = "notification_intent_submit_before_save";
        // Use EF's native current transaction here. The CAP unit-of-work wrapper does not expose savepoints,
        // while EF's relational transaction does and can recover from a duplicate unique-conflict inside an outer transaction.
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            Debug.Assert(transaction.SupportsSavepoints, "Notification duplicate recovery requires a transaction that supports savepoints.");
            await transaction.CreateSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.ReleaseSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
            }
        }
        catch (DbUpdateException exception)
        {
            if (!IsDuplicateIntentConflict(exception))
            {
                throw;
            }

            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(duplicateRecoverySavepoint, cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
            var duplicate = await repository.GetByDedupeKeyAsync(
                command.OrganizationId,
                command.EnvironmentId,
                request.SourceService,
                request.SourceEventType,
                request.DedupeKey,
                cancellationToken)
                ?? RethrowDuplicateConflict(exception);
            return duplicate.ToResponse(duplicate: true);
        }

        return intent.ToResponse(duplicate: false);
    }

    private bool IsDuplicateIntentConflict(DbUpdateException exception)
    {
        return dbContext.ChangeTracker.Entries<NotificationIntent>().Any(x => x.State == EntityState.Added)
            && ProcessedIntegrationEventInbox.IsUniqueConflict(exception, dbContext, constraintOrIndexName: null);
    }

    private static NotificationIntent RethrowDuplicateConflict(DbUpdateException exception)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        throw new InvalidOperationException("Unreachable duplicate conflict rethrow path.");
    }
}
