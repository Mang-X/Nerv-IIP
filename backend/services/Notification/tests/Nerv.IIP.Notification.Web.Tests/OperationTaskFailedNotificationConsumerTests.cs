using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class OperationTaskFailedNotificationConsumerTests
{
    [Fact]
    public async Task Handle_failed_operation_creates_notification_for_ops_admin()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleAsync(factory, CreateEvent("event-001", "operation-task-failed:task-001"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var message = Assert.Single(intent.Messages);
        var task = Assert.Single(intent.Tasks);
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal("org-001", intent.OrganizationId);
        Assert.Equal("env-001", intent.EnvironmentId);
        Assert.Equal("ops", intent.SourceService);
        Assert.Equal("ops.OperationTaskFailed", intent.SourceEventType);
        Assert.Equal("event-001", intent.SourceEventId);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityCritical, intent.Severity);
        Assert.Equal("operation-task-failed:task-001", intent.DedupeKey);
        Assert.Equal("operation-task", intent.ResourceType);
        Assert.Equal("task-001", intent.ResourceId);
        Assert.Equal("role:ops-admin", message.RecipientRef);
        Assert.Equal(message.Id, task.MessageId);
        Assert.Equal("notification.operation-task-failed", processed.ConsumerName);
        Assert.Equal("event-001", processed.EventId);
        Assert.Equal("operation-task-failed:task-001", processed.IdempotencyKey);
    }

    [Fact]
    public async Task Handle_completed_operation_creates_result_notification_for_ops_admin()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleCompletedAsync(factory, CreateCompletedEvent("event-completed", "operation-task-completed:task-001"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();

        Assert.Equal("ops.OperationTaskCompleted", intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Message, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityInfo, intent.Severity);
        Assert.Equal("operation-task", intent.ResourceType);
        Assert.Equal("task-001", intent.ResourceId);
        Assert.Equal("role:ops-admin", Assert.Single(intent.Messages).RecipientRef);
        Assert.Empty(intent.Tasks);
    }

    [Fact]
    public async Task Handle_approval_requested_creates_review_task_for_ops_admin()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleApprovalRequestedAsync(factory, CreateApprovalRequestedEvent("event-approval-requested", "operation-approval-requested:task-001"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();

        Assert.Equal("ops.OperationApprovalRequested", intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityWarning, intent.Severity);
        Assert.Equal("operation-task", intent.ResourceType);
        Assert.Equal("task-001", intent.ResourceId);
        Assert.Equal("role:ops-admin", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
    }

    [Fact]
    public async Task Handle_approval_decided_creates_message_for_ops_admin()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleApprovalApprovedAsync(factory, CreateApprovalApprovedEvent("event-approval-approved", "operation-approval-approved:task-001"));
        await HandleApprovalRejectedAsync(factory, CreateApprovalRejectedEvent("event-approval-rejected", "operation-approval-rejected:task-002", operationTaskId: "task-002"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intents = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .OrderBy(x => x.SourceEventType)
            .ToListAsync();

        Assert.Contains(intents, x => x.SourceEventType == "ops.OperationApprovalApproved"
            && x.IntentType == NotificationIntentTypes.Message
            && x.ResourceId == "task-001");
        Assert.Contains(intents, x => x.SourceEventType == "ops.OperationApprovalRejected"
            && x.IntentType == NotificationIntentTypes.Message
            && x.ResourceId == "task-002");
        Assert.All(intents, x => Assert.Empty(x.Tasks));
    }

    [Fact]
    public async Task Handle_approval_step_overdue_creates_review_task_for_step_approver()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleApprovalStepOverdueAsync(factory, CreateApprovalStepOverdueEvent("event-approval-overdue", "approval-step-overdue:chain-001:1"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();

        Assert.Equal("business-approval", intent.SourceService);
        Assert.Equal("businessApproval.StepOverdue", intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityWarning, intent.Severity);
        Assert.Equal("approval-chain", intent.ResourceType);
        Assert.Equal("chain-001", intent.ResourceId);
        Assert.Equal("user:u-engineering", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
    }

    [Fact]
    public async Task Handle_approval_step_overdue_includes_configured_escalation_recipients()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Approval:OverdueEscalation:RecipientRefs:0"] = "role:business-approval-manager",
        });

        await HandleApprovalStepOverdueAsync(factory, CreateApprovalStepOverdueEvent("event-approval-overdue", "approval-step-overdue:chain-001:1"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();

        Assert.Contains(intent.Messages, x => x.RecipientRef == "user:u-engineering");
        Assert.Contains(intent.Messages, x => x.RecipientRef == "role:business-approval-manager");
        Assert.Contains(intent.Tasks, x => x.RecipientRef == "user:u-engineering");
        Assert.Contains(intent.Tasks, x => x.RecipientRef == "role:business-approval-manager");
    }

    [Fact]
    public async Task Handle_approval_step_overdue_deduplicates_escalation_recipient_matching_approver()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Approval:OverdueEscalation:RecipientRefs:0"] = "user:u-engineering",
            ["Approval:OverdueEscalation:RecipientRefs:1"] = "role:business-approval-manager",
        });

        await HandleApprovalStepOverdueAsync(factory, CreateApprovalStepOverdueEvent("event-approval-overdue", "approval-step-overdue:chain-001:1"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .SingleAsync();

        Assert.Equal(2, intent.Messages.Count);
        Assert.Contains(intent.Messages, x => x.RecipientRef == "user:u-engineering");
        Assert.Contains(intent.Messages, x => x.RecipientRef == "role:business-approval-manager");
    }

    [Fact]
    public async Task Handle_approval_step_resolved_creates_result_message_for_actor()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleApprovalStepResolvedAsync(factory, CreateApprovalStepResolvedEvent("event-approval-resolved", "approval-step-resolved:chain-001:1"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();

        Assert.Equal("businessApproval.StepResolved", intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Message, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityInfo, intent.Severity);
        Assert.Equal("approval-chain", intent.ResourceType);
        Assert.Equal("chain-001", intent.ResourceId);
        Assert.Equal("user:u-engineering", Assert.Single(intent.Messages).RecipientRef);
        Assert.Empty(intent.Tasks);
    }

    [Fact]
    public async Task Handle_approval_rejected_notifies_initiator_once_on_redelivery()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateBusinessApprovalRejectedEvent();

        await HandleBusinessApprovalRejectedAsync(factory, integrationEvent);
        await HandleBusinessApprovalRejectedAsync(factory, integrationEvent);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents.Include(x => x.Messages).SingleAsync();
        Assert.Equal("businessApproval.ApprovalRejected", intent.SourceEventType);
        Assert.Equal("user:requester-001", Assert.Single(intent.Messages).RecipientRef);
    }

    [Fact]
    public async Task Handle_approval_action_recorded_creates_task_for_new_assignee()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleApprovalActionRecordedAsync(factory, CreateApprovalActionRecordedEvent("event-approval-action", "approval-action:chain-001:transfer"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();

        Assert.Equal("businessApproval.ActionRecorded", intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityInfo, intent.Severity);
        Assert.Equal("approval-chain", intent.ResourceType);
        Assert.Equal("chain-001", intent.ResourceId);
        Assert.Equal("user:u-backup", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
    }

    [Fact]
    public async Task Handle_approval_action_recorded_withdraw_creates_message_for_affected_approvers()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleApprovalActionRecordedAsync(factory, CreateApprovalActionRecordedEvent(
            "event-approval-withdraw",
            "approval-action:chain-001:withdraw",
            action: "withdraw",
            recipients: ["user:u-engineering", "user:u-quality"]));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();

        Assert.Equal(NotificationIntentTypes.Message, intent.IntentType);
        Assert.Contains(intent.Messages, x => x.RecipientRef == "user:u-engineering");
        Assert.Contains(intent.Messages, x => x.RecipientRef == "user:u-quality");
        Assert.Empty(intent.Tasks);
    }

    [Fact]
    public async Task Handle_schedule_conflict_detected_creates_planner_notification_task()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Scheduling:ConflictNotification:RecipientRefs:0"] = "role:scheduler",
        });

        await HandleScheduleConflictDetectedAsync(factory, CreateScheduleConflictDetectedEvent(
            "event-schedule-conflict",
            "scheduling-conflict:plan-001:conflict-001"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal("business-scheduling", intent.SourceService);
        Assert.Equal(SchedulingIntegrationEventTypes.ScheduleConflictDetected, intent.SourceEventType);
        Assert.Equal("event-schedule-conflict", intent.SourceEventId);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityWarning, intent.Severity);
        Assert.Equal("schedule-plan", intent.ResourceType);
        Assert.Equal("plan-001", intent.ResourceId);
        Assert.Equal("role:scheduler", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
        Assert.Equal(ScheduleConflictDetectedIntegrationEventHandlerForNotification.ConsumerName, processed.ConsumerName);
        Assert.Equal("event-schedule-conflict", processed.EventId);
        Assert.Equal("scheduling-conflict:plan-001:conflict-001", processed.IdempotencyKey);
    }

    [Fact]
    public async Task Handle_schedule_conflict_detected_uses_default_recipient_when_configuration_missing()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleScheduleConflictDetectedAsync(factory, CreateScheduleConflictDetectedEvent(
            "event-schedule-conflict-default-recipient",
            "scheduling-conflict:plan-001:conflict-default"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();

        Assert.Equal("role:production-planner", Assert.Single(intent.Messages).RecipientRef);
        Assert.Equal("role:production-planner", Assert.Single(intent.Tasks).RecipientRef);
    }

    [Fact]
    public async Task Handle_schedule_conflict_detected_error_severity_creates_critical_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleScheduleConflictDetectedAsync(factory, CreateScheduleConflictDetectedEvent(
            "event-schedule-conflict-error",
            "scheduling-conflict:plan-001:conflict-error",
            conflictSeverity: "error"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents.SingleAsync();

        Assert.Equal(NotificationContractConstants.SeverityCritical, intent.Severity);
    }

    [Fact]
    public async Task Handle_same_schedule_conflict_event_twice_does_not_create_duplicate_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateScheduleConflictDetectedEvent(
            "event-schedule-conflict-duplicate",
            "scheduling-conflict:plan-001:conflict-duplicate");

        await HandleScheduleConflictDetectedAsync(factory, integrationEvent);
        await HandleScheduleConflictDetectedAsync(factory, integrationEvent);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await dbContext.NotificationIntents.CountAsync());
        Assert.Equal(1, await dbContext.NotificationMessages.CountAsync());
        Assert.Equal(1, await dbContext.NotificationTasks.CountAsync());
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Handle_schedule_plan_invalidated_creates_planner_reschedule_task_once()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Scheduling:InvalidationNotification:RecipientRefs:0"] = "role:scheduler",
        });
        var integrationEvent = CreateSchedulePlanInvalidatedEvent(
            "event-schedule-invalidated",
            "scheduling-invalidated:plan-001:maintenance-event-001");

        await HandleSchedulePlanInvalidatedAsync(factory, integrationEvent);
        await HandleSchedulePlanInvalidatedAsync(factory, integrationEvent);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal("business-scheduling", intent.SourceService);
        Assert.Equal(SchedulingIntegrationEventTypes.SchedulePlanInvalidated, intent.SourceEventType);
        Assert.Equal("event-schedule-invalidated", intent.SourceEventId);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityWarning, intent.Severity);
        Assert.Equal("schedule-plan", intent.ResourceType);
        Assert.Equal("plan-001", intent.ResourceId);
        Assert.Equal("role:scheduler", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
        Assert.Contains("equipmentUnavailable", intent.Summary, StringComparison.Ordinal);
        Assert.Equal(SchedulePlanInvalidatedIntegrationEventHandlerForNotification.ConsumerName, processed.ConsumerName);
        Assert.Equal("scheduling-invalidated:plan-001:maintenance-event-001", processed.IdempotencyKey);
    }

    [Fact]
    public async Task Handle_schedule_conflict_detected_rejects_missing_severity_without_marking_processed()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateScheduleConflictDetectedEvent(
            "event-schedule-conflict-missing-severity",
            "scheduling-conflict:plan-001:conflict-missing-severity",
            conflictSeverity: null!);

        await Assert.ThrowsAsync<KnownException>(() => HandleScheduleConflictDetectedAsync(factory, integrationEvent));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Empty(await dbContext.NotificationIntents.ToListAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToListAsync());
    }

    [Fact]
    public async Task Handle_schedule_conflict_detected_omits_work_order_summary_when_work_order_missing()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleScheduleConflictDetectedAsync(factory, CreateScheduleConflictDetectedEvent(
            "event-schedule-conflict-no-work-order",
            "scheduling-conflict:plan-001:conflict-no-work-order",
            workOrderId: " "));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents.SingleAsync();

        Assert.Equal("Schedule plan plan-001 has conflict conflict-001 (dueDate).", intent.Summary);
    }

    [Fact]
    public async Task Handle_same_event_twice_does_not_create_duplicate_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateEvent("event-duplicate", "operation-task-failed:duplicate");

        await HandleAsync(factory, integrationEvent);
        await HandleAsync(factory, integrationEvent);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await dbContext.NotificationIntents.CountAsync());
        Assert.Equal(1, await dbContext.NotificationMessages.CountAsync());
        Assert.Equal(1, await dbContext.NotificationTasks.CountAsync());
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Handle_different_event_with_different_dedupe_creates_another_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleAsync(factory, CreateEvent("event-first", "operation-task-failed:first", operationTaskId: "task-first"));
        await HandleAsync(factory, CreateEvent("event-second", "operation-task-failed:second", operationTaskId: "task-second"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await dbContext.NotificationIntents.CountAsync());
        Assert.Equal(2, await dbContext.NotificationMessages.CountAsync());
        Assert.Equal(2, await dbContext.NotificationTasks.CountAsync());
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Handle_different_event_with_same_dedupe_records_processed_event_but_reuses_intent()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleAsync(factory, CreateEvent("event-retry-first", "operation-task-failed:retry", operationTaskId: "task-retry"));
        await HandleAsync(factory, CreateEvent("event-retry-second", "operation-task-failed:retry", operationTaskId: "task-retry"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await dbContext.NotificationIntents.CountAsync());
        Assert.Equal(1, await dbContext.NotificationMessages.CountAsync());
        Assert.Equal(1, await dbContext.NotificationTasks.CountAsync());
        var processed = Assert.Single(await dbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal("event-retry-first", processed.EventId);
        Assert.Equal("operation-task-failed:retry", processed.IdempotencyKey);
    }

    [Fact]
    public async Task Handle_event_missing_required_data_is_rejected_without_marking_processed()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateEvent("event-invalid", "operation-task-failed:invalid", operationTaskId: " ");

        await Assert.ThrowsAsync<KnownException>(() => HandleAsync(factory, integrationEvent));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.NotificationIntents.ToListAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToListAsync());
    }

    [Fact]
    public async Task Handle_unsupported_event_version_is_dead_lettered_without_creating_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleAsync(factory, CreateEvent("event-v2", "operation-task-failed:v2", eventVersion: 2));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetterStore = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            OperationTaskFailedIntegrationEventHandlerForNotification.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));

        Assert.Empty(await dbContext.NotificationIntents.ToListAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
    }

    [Fact]
    public void PostgreSQL_profile_uses_persistent_dead_letter_store()
    {
        using var factory = new NotificationPostgreSqlWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();

        Assert.IsType<PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>(store);
    }

    private static async Task HandleAsync(
        NotificationConsumerWebApplicationFactory factory,
        OperationTaskFailedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<OperationTaskFailedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<OperationTaskFailedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleCompletedAsync(
        NotificationConsumerWebApplicationFactory factory,
        OperationTaskCompletedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<OperationTaskCompletedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<OperationTaskCompletedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleApprovalRequestedAsync(
        NotificationConsumerWebApplicationFactory factory,
        OperationApprovalRequestedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<OperationApprovalRequestedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<OperationApprovalRequestedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleApprovalApprovedAsync(
        NotificationConsumerWebApplicationFactory factory,
        OperationApprovalApprovedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<OperationApprovalApprovedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<OperationApprovalApprovedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleApprovalRejectedAsync(
        NotificationConsumerWebApplicationFactory factory,
        OperationApprovalRejectedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<OperationApprovalRejectedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<OperationApprovalRejectedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleApprovalStepOverdueAsync(
        NotificationConsumerWebApplicationFactory factory,
        ApprovalStepOverdueIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<ApprovalStepOverdueIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<ApprovalStepOverdueIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleBusinessApprovalRejectedAsync(
        NotificationConsumerWebApplicationFactory factory,
        ApprovalCompletedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<ApprovalCompletedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<ApprovalRejectedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static ApprovalCompletedIntegrationEvent CreateBusinessApprovalRejectedEvent() => new(
        "event-business-approval-rejected",
        ApprovalIntegrationEventTypes.ApprovalRejected,
        ApprovalIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-07-11T00:00:00Z"),
        ApprovalIntegrationEventSources.BusinessApproval,
        "chain-001",
        "decision-001",
        "org-001",
        "env-dev",
        "user:approver-001",
        "approval-rejected:chain-001",
        new ApprovalCompletedPayload(
            "chain-001",
            ApprovalResults.Rejected,
            "user",
            "approver-001",
            null,
            null,
            new ApprovalDocumentReferencePayload("business-erp", "purchase-order", "PO-001", null),
            "user:requester-001"));

    private static async Task HandleApprovalStepResolvedAsync(
        NotificationConsumerWebApplicationFactory factory,
        ApprovalStepResolvedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<ApprovalStepResolvedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<ApprovalStepResolvedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleApprovalActionRecordedAsync(
        NotificationConsumerWebApplicationFactory factory,
        ApprovalActionRecordedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<ApprovalActionRecordedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<ApprovalActionRecordedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleScheduleConflictDetectedAsync(
        NotificationConsumerWebApplicationFactory factory,
        ScheduleConflictDetectedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<ScheduleConflictDetectedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<ScheduleConflictDetectedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleSchedulePlanInvalidatedAsync(
        NotificationConsumerWebApplicationFactory factory,
        SchedulePlanInvalidatedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<SchedulePlanInvalidatedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<SchedulePlanInvalidatedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static OperationTaskFailedIntegrationEvent CreateEvent(
        string eventId,
        string idempotencyKey,
        string operationTaskId = "task-001",
        int eventVersion = 1)
    {
        return new OperationTaskFailedIntegrationEvent(
            EventId: eventId,
            EventType: "ops.OperationTaskFailed",
            EventVersion: eventVersion,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:00Z"),
            SourceService: "ops",
            CorrelationId: $"corr-{eventId}",
            CausationId: $"cause-{eventId}",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "connector-host-001",
            IdempotencyKey: idempotencyKey,
            Payload: new OperationTaskFailedPayload(
                OperationTaskId: operationTaskId,
                AttemptId: $"attempt-{eventId}",
                InstanceKey: "demo-api-001",
                OperationCode: "lifecycle.restart",
                FinishedAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:05Z"),
                FailureCode: "timeout"));
    }

    private static OperationTaskCompletedIntegrationEvent CreateCompletedEvent(
        string eventId,
        string idempotencyKey,
        string operationTaskId = "task-001")
    {
        return new OperationTaskCompletedIntegrationEvent(
            EventId: eventId,
            EventType: "ops.OperationTaskCompleted",
            EventVersion: 1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:00Z"),
            SourceService: "ops",
            CorrelationId: $"corr-{eventId}",
            CausationId: $"cause-{eventId}",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "connector-host-001",
            IdempotencyKey: idempotencyKey,
            Payload: new OperationTaskCompletedPayload(
                OperationTaskId: operationTaskId,
                AttemptId: $"attempt-{eventId}",
                InstanceKey: "demo-api-001",
                OperationCode: "lifecycle.restart",
                FinishedAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:05Z")));
    }

    private static OperationApprovalRequestedIntegrationEvent CreateApprovalRequestedEvent(
        string eventId,
        string idempotencyKey,
        string operationTaskId = "task-001")
    {
        return new OperationApprovalRequestedIntegrationEvent(
            EventId: eventId,
            EventType: "ops.OperationApprovalRequested",
            EventVersion: 1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:00Z"),
            SourceService: "ops",
            CorrelationId: $"corr-{eventId}",
            CausationId: operationTaskId,
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "local-admin",
            IdempotencyKey: idempotencyKey,
            Payload: new OperationApprovalRequestedPayload(
                OperationTaskId: operationTaskId,
                InstanceKey: "demo-api-001",
                OperationCode: "lifecycle.restart",
                RequestedBy: "local-admin",
                RequestedAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:00Z")));
    }

    private static OperationApprovalApprovedIntegrationEvent CreateApprovalApprovedEvent(
        string eventId,
        string idempotencyKey,
        string operationTaskId = "task-001")
    {
        return new OperationApprovalApprovedIntegrationEvent(
            EventId: eventId,
            EventType: "ops.OperationApprovalApproved",
            EventVersion: 1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-21T08:01:00Z"),
            SourceService: "ops",
            CorrelationId: $"corr-{eventId}",
            CausationId: operationTaskId,
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "ops-approver",
            IdempotencyKey: idempotencyKey,
            Payload: new OperationApprovalDecidedPayload(
                OperationTaskId: operationTaskId,
                InstanceKey: "demo-api-001",
                OperationCode: "lifecycle.restart",
                DecidedBy: "ops-approver",
                DecisionReason: "approved",
                DecidedAtUtc: DateTimeOffset.Parse("2026-05-21T08:01:00Z")));
    }

    private static OperationApprovalRejectedIntegrationEvent CreateApprovalRejectedEvent(
        string eventId,
        string idempotencyKey,
        string operationTaskId = "task-001")
    {
        return new OperationApprovalRejectedIntegrationEvent(
            EventId: eventId,
            EventType: "ops.OperationApprovalRejected",
            EventVersion: 1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-21T08:01:00Z"),
            SourceService: "ops",
            CorrelationId: $"corr-{eventId}",
            CausationId: operationTaskId,
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "ops-approver",
            IdempotencyKey: idempotencyKey,
            Payload: new OperationApprovalDecidedPayload(
                OperationTaskId: operationTaskId,
                InstanceKey: "demo-api-001",
                OperationCode: "lifecycle.restart",
                DecidedBy: "ops-approver",
                DecisionReason: "rejected",
                DecidedAtUtc: DateTimeOffset.Parse("2026-05-21T08:01:00Z")));
    }

    private static ApprovalStepOverdueIntegrationEvent CreateApprovalStepOverdueEvent(
        string eventId,
        string idempotencyKey)
    {
        return new ApprovalStepOverdueIntegrationEvent(
            EventId: eventId,
            EventType: ApprovalIntegrationEventTypes.StepOverdue,
            EventVersion: ApprovalIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-06-21T08:00:00Z"),
            SourceService: ApprovalIntegrationEventSources.BusinessApproval,
            CorrelationId: $"corr-{eventId}",
            CausationId: "step-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:business-approval",
            IdempotencyKey: idempotencyKey,
            Payload: new ApprovalStepOverduePayload(
                ChainId: "chain-001",
                StepId: "step-001",
                StepNo: 1,
                StepName: "Engineering review",
                ApproverType: "user",
                ApproverRef: "u-engineering",
                DueAtUtc: DateTimeOffset.Parse("2026-06-21T07:00:00Z"),
                MarkedAtUtc: DateTimeOffset.Parse("2026-06-21T08:00:00Z"),
                DocumentReference: NewApprovalDocumentReference()));
    }

    private static ApprovalStepResolvedIntegrationEvent CreateApprovalStepResolvedEvent(
        string eventId,
        string idempotencyKey)
    {
        return new ApprovalStepResolvedIntegrationEvent(
            EventId: eventId,
            EventType: ApprovalIntegrationEventTypes.StepResolved,
            EventVersion: ApprovalIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-06-21T08:05:00Z"),
            SourceService: ApprovalIntegrationEventSources.BusinessApproval,
            CorrelationId: $"corr-{eventId}",
            CausationId: "decision-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "user:u-engineering",
            IdempotencyKey: idempotencyKey,
            Payload: new ApprovalStepResolvedPayload(
                ChainId: "chain-001",
                StepNo: 1,
                ActorType: "user",
                ActorRef: "u-engineering",
                OnBehalfOfActorType: null,
                OnBehalfOfActorRef: null,
                Decision: "approve",
                Comment: "ok",
                DocumentReference: NewApprovalDocumentReference()));
    }

    private static ApprovalActionRecordedIntegrationEvent CreateApprovalActionRecordedEvent(
        string eventId,
        string idempotencyKey,
        string action = "transfer",
        IReadOnlyCollection<string>? recipients = null)
    {
        return new ApprovalActionRecordedIntegrationEvent(
            EventId: eventId,
            EventType: ApprovalIntegrationEventTypes.ActionRecorded,
            EventVersion: ApprovalIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-06-21T08:10:00Z"),
            SourceService: ApprovalIntegrationEventSources.BusinessApproval,
            CorrelationId: "chain-001",
            CausationId: $"decision-{eventId}",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "user:u-manager",
            IdempotencyKey: idempotencyKey,
            Payload: new ApprovalActionRecordedPayload(
                ChainId: "chain-001",
                StepId: "step-001",
                StepNo: 1,
                Action: action,
                ActorType: "user",
                ActorRef: "u-manager",
                Reason: "shift change",
                SuggestedRecipientRefs: recipients ?? ["user:u-backup"],
                DocumentReference: NewApprovalDocumentReference()));
    }

    private static ApprovalDocumentReferencePayload NewApprovalDocumentReference()
    {
        return new ApprovalDocumentReferencePayload(
            SourceService: "eco",
            DocumentType: "engineering-change-order",
            DocumentId: "ECO-1001",
            DocumentLineId: null);
    }

    private static ScheduleConflictDetectedIntegrationEvent CreateScheduleConflictDetectedEvent(
        string eventId,
        string idempotencyKey,
        string? conflictSeverity = "warning",
        string? workOrderId = "wo-001")
    {
        return new ScheduleConflictDetectedIntegrationEvent(
            EventId: eventId,
            EventType: SchedulingIntegrationEventTypes.ScheduleConflictDetected,
            EventVersion: SchedulingIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-06-21T09:00:00Z"),
            SourceService: SchedulingIntegrationEventSources.BusinessScheduling,
            CorrelationId: $"corr-{eventId}",
            CausationId: "plan-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:business-scheduling",
            IdempotencyKey: idempotencyKey,
            Payload: new ScheduleConflictDetectedPayload(
                PlanId: "plan-001",
                ProblemId: "problem-001",
                ContractVersion: 1,
                AlgorithmVersion: "aps-lite-v1",
                ProblemFingerprint: "fingerprint-001",
                PlanStatus: "generated",
                ConflictId: "conflict-001",
                ConflictReasonCode: "dueDate",
                ConflictSeverity: conflictSeverity!,
                WorkOrderId: workOrderId!,
                OperationId: "op-001",
                ResourceId: "res-001"));
    }

    private static SchedulePlanInvalidatedIntegrationEvent CreateSchedulePlanInvalidatedEvent(
        string eventId,
        string idempotencyKey)
    {
        return new SchedulePlanInvalidatedIntegrationEvent(
            EventId: eventId,
            EventType: SchedulingIntegrationEventTypes.SchedulePlanInvalidated,
            EventVersion: SchedulingIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-06-21T09:05:00Z"),
            SourceService: SchedulingIntegrationEventSources.BusinessScheduling,
            CorrelationId: $"corr-{eventId}",
            CausationId: "maintenance-event-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:business-scheduling",
            IdempotencyKey: idempotencyKey,
            Payload: new SchedulePlanInvalidatedPayload(
                PlanId: "plan-001",
                ProblemId: "problem-001",
                ContractVersion: 1,
                AlgorithmVersion: "aps-lite-v1",
                ProblemFingerprint: "fingerprint-001",
                PlanStatus: "generated",
                ReasonCode: "equipmentUnavailable",
                SourceEventType: "maintenance.AssetUnavailable",
                SourceEventId: "maintenance-event-001",
                AffectedResourceIds: ["DEV-OIL-01"],
                AffectedOperations:
                [
                    new SchedulePlanAffectedOperationPayload(
                        WorkOrderId: "WO-APS-001",
                        OperationId: "OP-10",
                        OperationSequence: 10,
                        ResourceId: "DEV-OIL-01",
                        WorkCenterId: "WC-OIL",
                        StartUtc: DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
                        EndUtc: DateTimeOffset.Parse("2026-06-01T13:30:00Z"))
                ]));
    }

    private sealed class NotificationConsumerWebApplicationFactory(IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var mergedSettings = new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                };
                if (settings is not null)
                {
                    foreach (var (key, value) in settings)
                    {
                        mergedSettings[key] = value;
                    }
                }

                configuration.AddInMemoryCollection(mergedSettings);
            });
        }
    }

    private sealed class NotificationPostgreSqlWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            var settings = new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "PostgreSQL",
                ["ConnectionStrings:NotificationDb"] = "Host=localhost;Database=nerv_iip_notification_dead_letter_test;Username=nerv;Password=nerv",
                ["InternalService:BearerToken"] = "test-internal-token",
            };
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(settings);
            });
        }
    }
}
