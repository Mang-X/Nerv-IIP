using System.Net.Http;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlCommandAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;
using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryDeviceControlReadFaceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 8, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task History_filters_by_device_status_and_time_window_with_total()
    {
        await using var dbContext = CreateDbContext(nameof(History_filters_by_device_status_and_time_window_with_total));
        await RecordAsync(dbContext, "op-a1", "DEV-A", "approval-pending", BaseTime);
        await RecordAsync(dbContext, "op-a2", "DEV-A", "succeeded", BaseTime.AddMinutes(10));
        await RecordAsync(dbContext, "op-b1", "DEV-B", "succeeded", BaseTime.AddMinutes(20));
        await RecordAsync(dbContext, "op-a3", "DEV-A", "failed", BaseTime.AddMinutes(30));
        await RecordAsync(dbContext, "op-a4", "DEV-A", "succeeded", BaseTime.AddMinutes(40));

        var result = await new ListDeviceControlCommandsQueryHandler(dbContext).Handle(
            new ListDeviceControlCommandsQuery(
                "org-001",
                "env-dev",
                "DEV-A",
                "succeeded",
                BaseTime.AddMinutes(5),
                BaseTime.AddMinutes(45)),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(new[] { "op-a4", "op-a2" }, result.Items.Select(x => x.CommandId).ToArray());
    }

    [Fact]
    public async Task History_orders_by_requested_time_descending_and_pages()
    {
        await using var dbContext = CreateDbContext(nameof(History_orders_by_requested_time_descending_and_pages));
        await RecordAsync(dbContext, "op-1", "DEV-A", "approval-pending", BaseTime);
        await RecordAsync(dbContext, "op-2", "DEV-A", "approval-pending", BaseTime.AddMinutes(10));
        await RecordAsync(dbContext, "op-3", "DEV-A", "approval-pending", BaseTime.AddMinutes(20));

        var result = await new ListDeviceControlCommandsQueryHandler(dbContext).Handle(
            new ListDeviceControlCommandsQuery("org-001", "env-dev", null, null, null, null, Skip: 0, Take: 2),
            CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal(new[] { "op-3", "op-2" }, result.Items.Select(x => x.CommandId).ToArray());
    }

    [RealPostgresFact]
    public async Task History_filters_by_device_status_and_time_window_on_postgres()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await IndustrialTelemetryPostgresTestDatabase.CreateAsync(postgresConnectionString);

        await using (var setupContext = database.CreateContext())
        {
            setupContext.DeviceControlCommands.Add(NewCommand("op-pg-1", "DEV-PG-A", "approval-pending", BaseTime));
            setupContext.DeviceControlCommands.Add(NewCommand("op-pg-2", "DEV-PG-A", "succeeded", BaseTime.AddMinutes(10)));
            setupContext.DeviceControlCommands.Add(NewCommand("op-pg-3", "DEV-PG-B", "succeeded", BaseTime.AddMinutes(20)));
            setupContext.DeviceControlCommands.Add(NewCommand("op-pg-4", "DEV-PG-A", "succeeded", BaseTime.AddMinutes(90)));
            await setupContext.SaveChangesAsync();
        }

        await using var queryContext = database.CreateContext();
        var result = await new ListDeviceControlCommandsQueryHandler(queryContext).Handle(
            new ListDeviceControlCommandsQuery(
                "org-001",
                "env-dev",
                "DEV-PG-A",
                "succeeded",
                BaseTime.AddMinutes(5),
                BaseTime.AddMinutes(30)),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("op-pg-2", Assert.Single(result.Items).CommandId);
    }

    [Fact]
    public async Task History_orders_deterministically_when_requested_time_ties()
    {
        await using var dbContext = CreateDbContext(nameof(History_orders_deterministically_when_requested_time_ties));
        await RecordAsync(dbContext, "op-b", "DEV-A", "approval-pending", BaseTime);
        await RecordAsync(dbContext, "op-a", "DEV-A", "approval-pending", BaseTime);
        await RecordAsync(dbContext, "op-c", "DEV-A", "approval-pending", BaseTime);

        var result = await new ListDeviceControlCommandsQueryHandler(dbContext).Handle(
            new ListDeviceControlCommandsQuery("org-001", "env-dev", null, null, null, null),
            CancellationToken.None);

        Assert.Equal(new[] { "op-c", "op-b", "op-a" }, result.Items.Select(x => x.CommandId).ToArray());
    }

    [Fact]
    public async Task History_status_filter_is_case_insensitive()
    {
        await using var dbContext = CreateDbContext(nameof(History_status_filter_is_case_insensitive));
        await RecordAsync(dbContext, "op-1", "DEV-A", "approval-pending", BaseTime);

        var result = await new ListDeviceControlCommandsQueryHandler(dbContext).Handle(
            new ListDeviceControlCommandsQuery("org-001", "env-dev", null, "Approval-Pending", null, null),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task Result_enriches_ledger_with_live_ops_status_and_receipt()
    {
        await using var dbContext = CreateDbContext(nameof(Result_enriches_ledger_with_live_ops_status_and_receipt));
        await RecordAsync(dbContext, "op-live", "DEV-A", "approval-pending", BaseTime);
        var opsClient = new StubDeviceControlOpsClient(_ => CompletedTask("op-live"));

        var result = await new GetDeviceControlCommandQueryHandler(dbContext, opsClient).Handle(
            new GetDeviceControlCommandQuery("op-live", "org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal("op-live", result.CommandId);
        Assert.Equal("succeeded", result.Status);
        Assert.True(result.StatusFromLiveOps);
        Assert.Equal("approved", result.Approval?.Status);
        var attempt = Assert.Single(result.Attempts);
        Assert.Equal("ok", attempt.Output?["result"]);
    }

    [Fact]
    public async Task Result_falls_back_to_ledger_snapshot_when_ops_unavailable()
    {
        await using var dbContext = CreateDbContext(nameof(Result_falls_back_to_ledger_snapshot_when_ops_unavailable));
        await RecordAsync(dbContext, "op-down", "DEV-A", "approval-pending", BaseTime, approvalStatus: "pending");
        var opsClient = new StubDeviceControlOpsClient(_ => null);

        var result = await new GetDeviceControlCommandQueryHandler(dbContext, opsClient).Handle(
            new GetDeviceControlCommandQuery("op-down", "org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal("approval-pending", result.Status);
        Assert.False(result.StatusFromLiveOps);
        Assert.Empty(result.Attempts);
        Assert.Equal("pending", result.Approval?.Status);
    }

    [Fact]
    public async Task Result_throws_known_exception_when_command_not_found()
    {
        await using var dbContext = CreateDbContext(nameof(Result_throws_known_exception_when_command_not_found));
        var opsClient = new StubDeviceControlOpsClient(_ => null);

        await Assert.ThrowsAsync<KnownException>(() => new GetDeviceControlCommandQueryHandler(dbContext, opsClient).Handle(
            new GetDeviceControlCommandQuery("op-missing", "org-001", "env-dev"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Result_does_not_leak_command_across_organization_scope()
    {
        await using var dbContext = CreateDbContext(nameof(Result_does_not_leak_command_across_organization_scope));
        await RecordAsync(dbContext, "op-scope", "DEV-A", "approval-pending", BaseTime);
        var opsClient = new StubDeviceControlOpsClient(_ => null);

        await Assert.ThrowsAsync<KnownException>(() => new GetDeviceControlCommandQueryHandler(dbContext, opsClient).Handle(
            new GetDeviceControlCommandQuery("op-scope", "org-999", "env-dev"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Result_maps_parameter_set_parameters_from_ledger()
    {
        await using var dbContext = CreateDbContext(nameof(Result_maps_parameter_set_parameters_from_ledger));
        dbContext.DeviceControlCommands.Add(DeviceControlCommand.Record(
            "op-params",
            "org-001",
            "env-dev",
            "connector-host-001",
            "opcua-cell-01",
            "DEV-A",
            "parameter-set",
            tagKey: null,
            value: null,
            parametersJson: "{\"spindle.speed\":\"80\",\"feed.rate\":\"120\"}",
            "user:operator-001",
            "recipe change",
            "idem-params",
            "corr-params",
            "approval-pending",
            approvalStatus: "pending",
            BaseTime));
        await dbContext.SaveChangesAsync();
        var opsClient = new StubDeviceControlOpsClient(_ => null);

        var result = await new GetDeviceControlCommandQueryHandler(dbContext, opsClient).Handle(
            new GetDeviceControlCommandQuery("op-params", "org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal("parameter-set", result.CommandType);
        Assert.NotNull(result.Parameters);
        Assert.Equal("80", result.Parameters!["spindle.speed"]);
        Assert.Equal("120", result.Parameters["feed.rate"]);
    }

    private static async Task RecordAsync(
        ApplicationDbContext dbContext,
        string operationTaskId,
        string deviceAssetId,
        string status,
        DateTimeOffset requestedAtUtc,
        string? approvalStatus = null)
    {
        dbContext.DeviceControlCommands.Add(NewCommand(operationTaskId, deviceAssetId, status, requestedAtUtc, approvalStatus));
        await dbContext.SaveChangesAsync();
    }

    private static DeviceControlCommand NewCommand(
        string operationTaskId,
        string deviceAssetId,
        string status,
        DateTimeOffset requestedAtUtc,
        string? approvalStatus = null)
    {
        return DeviceControlCommand.Record(
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
            approvalStatus,
            requestedAtUtc);
    }

    private static OperationTaskResponse CompletedTask(string operationTaskId)
    {
        return new OperationTaskResponse(
            operationTaskId,
            "org-001",
            "env-dev",
            "opcua-cell-01",
            "device.control.command",
            "succeeded",
            "user:operator-001",
            BaseTime,
            new OperationApprovalSummary("approved", "user:operator-001", BaseTime, "user:supervisor-001", BaseTime.AddMinutes(2), "approved"),
            "attempt-001",
            [
                new OperationAttemptSummary(
                    "attempt-001",
                    "succeeded",
                    BaseTime.AddMinutes(3),
                    BaseTime.AddMinutes(5),
                    null,
                    "lease-001",
                    BaseTime.AddMinutes(3),
                    BaseTime.AddMinutes(8),
                    1,
                    300,
                    3,
                    null,
                    new Dictionary<string, string> { ["result"] = "ok" }),
            ],
            []);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class StubDeviceControlOpsClient(Func<string, OperationTaskResponse?> getBehavior) : IDeviceControlOpsClient
    {
        public Task<OperationTaskResponse> CreateDeviceControlTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationTaskResponse> GetDeviceControlTaskAsync(string operationTaskId, CancellationToken cancellationToken)
        {
            var response = getBehavior(operationTaskId);
            return response is null
                ? throw new HttpRequestException($"Operation task unavailable: {operationTaskId}")
                : Task.FromResult(response);
        }
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
