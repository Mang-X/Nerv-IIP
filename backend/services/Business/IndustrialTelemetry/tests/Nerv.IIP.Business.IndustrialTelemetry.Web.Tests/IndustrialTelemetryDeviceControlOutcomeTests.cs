using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlCommandAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryDeviceControlOutcomeTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 8, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Advance_status_command_moves_ledger_to_completed_outcome()
    {
        await using var dbContext = CreateDbContext(nameof(Advance_status_command_moves_ledger_to_completed_outcome));
        await SeedAsync(dbContext, "op-c1", "DEV-A", "approval-pending");

        var advanced = await new AdvanceDeviceControlCommandStatusCommandHandler(dbContext).Handle(
            new AdvanceDeviceControlCommandStatusCommand("op-c1", "completed", BaseTime.AddMinutes(5), null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(advanced);
        var row = await dbContext.DeviceControlCommands.SingleAsync();
        Assert.Equal("completed", row.Status);
        Assert.Equal(BaseTime.AddMinutes(5), row.FinishedAtUtc);
        Assert.Null(row.FailureCode);
    }

    [Fact]
    public async Task Advance_status_command_records_failure_code()
    {
        await using var dbContext = CreateDbContext(nameof(Advance_status_command_records_failure_code));
        await SeedAsync(dbContext, "op-f1", "DEV-A", "queued");

        await new AdvanceDeviceControlCommandStatusCommandHandler(dbContext).Handle(
            new AdvanceDeviceControlCommandStatusCommand("op-f1", "failed", BaseTime.AddMinutes(5), "device-timeout"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var row = await dbContext.DeviceControlCommands.SingleAsync();
        Assert.Equal("failed", row.Status);
        Assert.Equal("device-timeout", row.FailureCode);
    }

    [Fact]
    public async Task Advance_status_command_is_idempotent_once_terminal()
    {
        await using var dbContext = CreateDbContext(nameof(Advance_status_command_is_idempotent_once_terminal));
        await SeedAsync(dbContext, "op-t1", "DEV-A", "approval-pending");
        var handler = new AdvanceDeviceControlCommandStatusCommandHandler(dbContext);

        await handler.Handle(new AdvanceDeviceControlCommandStatusCommand("op-t1", "completed", BaseTime.AddMinutes(5), null), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var secondAdvance = await handler.Handle(new AdvanceDeviceControlCommandStatusCommand("op-t1", "failed", BaseTime.AddMinutes(9), "late"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.False(secondAdvance);
        var row = await dbContext.DeviceControlCommands.SingleAsync();
        Assert.Equal("completed", row.Status);
        Assert.Null(row.FailureCode);
    }

    [Fact]
    public async Task Advance_status_command_no_ops_for_unknown_command()
    {
        await using var dbContext = CreateDbContext(nameof(Advance_status_command_no_ops_for_unknown_command));

        var advanced = await new AdvanceDeviceControlCommandStatusCommandHandler(dbContext).Handle(
            new AdvanceDeviceControlCommandStatusCommand("op-missing", "completed", BaseTime, null),
            CancellationToken.None);

        Assert.False(advanced);
    }

    [Fact]
    public async Task Completed_handler_advances_only_device_control_operation_tasks()
    {
        var deviceControlSender = new CapturingSender();
        var deviceControlHandler = new DeviceControlCommandCompletedHandler(deviceControlSender, new InMemoryIntegrationEventDeadLetterStore());
        await deviceControlHandler.HandleAsync(CompletedEvent("op-c1", "device.control.command"), CancellationToken.None);

        var otherSender = new CapturingSender();
        var otherHandler = new DeviceControlCommandCompletedHandler(otherSender, new InMemoryIntegrationEventDeadLetterStore());
        await otherHandler.HandleAsync(CompletedEvent("op-restart", "instance.restart"), CancellationToken.None);

        var command = Assert.IsType<AdvanceDeviceControlCommandStatusCommand>(Assert.Single(deviceControlSender.Sent));
        Assert.Equal("op-c1", command.OperationTaskId);
        Assert.Equal("completed", command.TerminalStatus);
        Assert.Empty(otherSender.Sent);
    }

    [Fact]
    public async Task History_status_filter_reflects_completed_outcome_after_advance()
    {
        await using var dbContext = CreateDbContext(nameof(History_status_filter_reflects_completed_outcome_after_advance));
        await SeedAsync(dbContext, "op-h1", "DEV-A", "approval-pending");
        await new AdvanceDeviceControlCommandStatusCommandHandler(dbContext).Handle(
            new AdvanceDeviceControlCommandStatusCommand("op-h1", "completed", BaseTime.AddMinutes(5), null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var completed = await new ListDeviceControlCommandsQueryHandler(dbContext).Handle(
            new ListDeviceControlCommandsQuery("org-001", "env-dev", "DEV-A", "completed", null, null),
            CancellationToken.None);
        var stillPending = await new ListDeviceControlCommandsQueryHandler(dbContext).Handle(
            new ListDeviceControlCommandsQuery("org-001", "env-dev", "DEV-A", "approval-pending", null, null),
            CancellationToken.None);

        Assert.Equal(1, completed.Total);
        Assert.Equal("op-h1", Assert.Single(completed.Items).CommandId);
        Assert.Equal(0, stillPending.Total);
    }

    [Fact]
    public async Task Advance_status_command_projects_approval_terminal_on_completion()
    {
        await using var dbContext = CreateDbContext(nameof(Advance_status_command_projects_approval_terminal_on_completion));
        await SeedAsync(dbContext, "op-ap1", "DEV-A", "approval-pending");

        await new AdvanceDeviceControlCommandStatusCommandHandler(dbContext).Handle(
            new AdvanceDeviceControlCommandStatusCommand("op-ap1", "completed", BaseTime.AddMinutes(5), null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var row = await dbContext.DeviceControlCommands.SingleAsync();
        Assert.Equal("completed", row.Status);
        // A still-pending approval snapshot that reaches a terminal execution outcome must read as approved.
        Assert.Equal("approved", row.ApprovalStatus);
    }

    [Fact]
    public async Task Rejected_handler_projects_rejected_status_and_approval()
    {
        var sender = new CapturingSender();
        var handler = new DeviceControlCommandRejectedHandler(sender, new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(RejectedEvent("op-r1", "device.control.command"), CancellationToken.None);

        var otherSender = new CapturingSender();
        var otherHandler = new DeviceControlCommandRejectedHandler(otherSender, new InMemoryIntegrationEventDeadLetterStore());
        await otherHandler.HandleAsync(RejectedEvent("op-restart", "instance.restart"), CancellationToken.None);

        var command = Assert.IsType<AdvanceDeviceControlCommandStatusCommand>(Assert.Single(sender.Sent));
        Assert.Equal("op-r1", command.OperationTaskId);
        Assert.Equal("rejected", command.TerminalStatus);
        Assert.Empty(otherSender.Sent);

        await using var dbContext = CreateDbContext(nameof(Rejected_handler_projects_rejected_status_and_approval));
        await SeedAsync(dbContext, "op-r1", "DEV-A", "approval-pending");
        await new AdvanceDeviceControlCommandStatusCommandHandler(dbContext).Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var row = await dbContext.DeviceControlCommands.SingleAsync();
        Assert.Equal("rejected", row.Status);
        Assert.Equal("rejected", row.ApprovalStatus);
    }

    private static async Task SeedAsync(ApplicationDbContext dbContext, string operationTaskId, string deviceAssetId, string status)
    {
        dbContext.DeviceControlCommands.Add(DeviceControlCommand.Record(
            operationTaskId,
            "org-001",
            "env-dev",
            "connector-host-001",
            "opcua-cell-01",
            deviceAssetId,
            "write-tag",
            "spindle.speed",
            "80",
            parametersJson: null,
            "user:operator-001",
            "speed adjustment",
            $"idem-{operationTaskId}",
            $"corr-{operationTaskId}",
            status,
            approvalStatus: status == "approval-pending" ? "pending" : null,
            BaseTime));
        await dbContext.SaveChangesAsync();
    }

    private static OperationTaskCompletedIntegrationEvent CompletedEvent(string operationTaskId, string operationCode)
    {
        return new OperationTaskCompletedIntegrationEvent(
            $"evt-{operationTaskId}",
            "ops.OperationTaskCompleted",
            1,
            BaseTime.AddMinutes(5),
            "ops",
            $"corr-{operationTaskId}",
            $"cause-{operationTaskId}",
            "org-001",
            "env-dev",
            "connector-host-001",
            $"ops:operation-task-completed:{operationTaskId}",
            new OperationTaskCompletedPayload(operationTaskId, "attempt-001", "opcua-cell-01", operationCode, BaseTime.AddMinutes(5)));
    }

    private static OperationApprovalRejectedIntegrationEvent RejectedEvent(string operationTaskId, string operationCode)
    {
        return new OperationApprovalRejectedIntegrationEvent(
            $"evt-{operationTaskId}",
            "ops.OperationApprovalRejected",
            1,
            BaseTime.AddMinutes(3),
            "ops",
            $"corr-{operationTaskId}",
            $"cause-{operationTaskId}",
            "org-001",
            "env-dev",
            "user:supervisor-001",
            $"ops:operation-approval-rejected:{operationTaskId}",
            new OperationApprovalDecidedPayload(operationTaskId, "opcua-cell-01", operationCode, "user:supervisor-001", "not safe to write", BaseTime.AddMinutes(3)));
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class CapturingSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult(default(TResponse)!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult<object?>(null);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
