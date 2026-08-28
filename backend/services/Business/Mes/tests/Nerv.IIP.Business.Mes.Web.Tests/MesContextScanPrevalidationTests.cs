using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesContextScanPrevalidationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-28T01:00:00Z");

    [Fact]
    public async Task Operation_scan_accepts_only_the_exact_current_operation_task()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.OperationTasks.Add(NewTask());
        await dbContext.SaveChangesAsync();
        var handler = new PrevalidateContextScanQueryHandler(
            dbContext,
            AcceptingQualificationGate.Instance,
            new FakeTimeProvider(Now));

        var accepted = await handler.Handle(
            Request(MesContextScanObjectType.OperationTask, "OP-10"),
            CancellationToken.None);
        var rejected = await handler.Handle(
            Request(MesContextScanObjectType.OperationTask, "OP-20"),
            CancellationToken.None);

        Assert.Equal(MesContextScanDecision.Accepted, accepted.Decision);
        Assert.Equal("operation-task-scan-accepted", accepted.ReasonCode);
        Assert.Equal(MesContextScanDecision.Rejected, rejected.Decision);
        Assert.Equal("operation-task-mismatch", rejected.ReasonCode);
    }

    [Fact]
    public async Task Device_scan_accepts_only_the_device_assigned_to_the_current_operation_task()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.OperationTasks.Add(NewTask());
        await dbContext.SaveChangesAsync();
        var handler = new PrevalidateContextScanQueryHandler(
            dbContext,
            AcceptingQualificationGate.Instance,
            new FakeTimeProvider(Now));

        var accepted = await handler.Handle(
            Request(MesContextScanObjectType.DeviceAsset, "device-001"),
            CancellationToken.None);
        var rejected = await handler.Handle(
            Request(MesContextScanObjectType.DeviceAsset, "device-002"),
            CancellationToken.None);

        Assert.Equal(MesContextScanDecision.Accepted, accepted.Decision);
        Assert.Equal("device-asset-scan-accepted", accepted.ReasonCode);
        Assert.Equal(MesContextScanObjectType.DeviceAsset, accepted.ObjectType);
        Assert.Equal(MesContextScanDecision.Rejected, rejected.Decision);
        Assert.Equal("device-asset-mismatch", rejected.ReasonCode);
    }

    [Fact]
    public async Task Personnel_scan_reuses_the_current_worker_skill_qualification_gate()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.OperationTasks.Add(NewTask());
        await dbContext.SaveChangesAsync();
        var qualificationGate = new RecordingQualificationGate();
        var handler = new PrevalidateContextScanQueryHandler(
            dbContext,
            qualificationGate,
            new FakeTimeProvider(Now));

        var response = await handler.Handle(
            Request(MesContextScanObjectType.Personnel, "worker-001"),
            CancellationToken.None);

        Assert.Equal(MesContextScanDecision.Accepted, response.Decision);
        Assert.Equal("personnel-scan-accepted", response.ReasonCode);
        Assert.Equal(MesContextScanObjectType.Personnel, response.ObjectType);
        var call = Assert.Single(qualificationGate.Calls);
        Assert.Equal(("org-001", "env-dev", "worker-001", "cnc-operation"), call);
    }

    [Fact]
    public async Task Personnel_mismatch_is_rejected_without_querying_the_qualification_source()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.OperationTasks.Add(NewTask());
        await dbContext.SaveChangesAsync();
        var qualificationGate = new RecordingQualificationGate();
        var handler = new PrevalidateContextScanQueryHandler(
            dbContext,
            qualificationGate,
            new FakeTimeProvider(Now));

        var response = await handler.Handle(
            Request(MesContextScanObjectType.Personnel, "worker-002"),
            CancellationToken.None);

        Assert.Equal(MesContextScanDecision.Rejected, response.Decision);
        Assert.Equal("personnel-mismatch", response.ReasonCode);
        Assert.Empty(qualificationGate.Calls);
    }

    [Theory]
    [InlineData("人员 'worker-001' 已停用，不能派工或开工。")]
    [InlineData("WORKER_SKILL_SOURCE_UNAVAILABLE: MasterData 人员资格来源暂不可用。")]
    public async Task Personnel_qualification_rejection_or_source_failure_remains_distinguishable(string message)
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.OperationTasks.Add(NewTask());
        await dbContext.SaveChangesAsync();
        var handler = new PrevalidateContextScanQueryHandler(
            dbContext,
            new RejectingQualificationGate(message),
            new FakeTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(
                Request(MesContextScanObjectType.Personnel, "worker-001"),
                CancellationToken.None));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public async Task Personnel_qualification_propagates_cancellation()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.OperationTasks.Add(NewTask());
        await dbContext.SaveChangesAsync();
        var qualificationGate = new CancellingQualificationGate();
        var handler = new PrevalidateContextScanQueryHandler(
            dbContext,
            qualificationGate,
            new FakeTimeProvider(Now));
        using var cancellationTokenSource = new CancellationTokenSource();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(
                Request(MesContextScanObjectType.Personnel, "worker-001"),
                cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
        Assert.Equal(cancellationTokenSource.Token, qualificationGate.CancellationToken);
    }

    [Theory]
    [InlineData("org-other", "env-dev", "WO-001", "OP-10")]
    [InlineData("org-001", "env-other", "WO-001", "OP-10")]
    [InlineData("org-001", "env-dev", "WO-OTHER", "OP-10")]
    [InlineData("org-001", "env-dev", "WO-001", "OP-OTHER")]
    public async Task Context_lookup_rejects_each_mismatched_scope_or_task_fact(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId)
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.OperationTasks.Add(NewTask());
        await dbContext.SaveChangesAsync();
        var handler = new PrevalidateContextScanQueryHandler(
            dbContext,
            AcceptingQualificationGate.Instance,
            new FakeTimeProvider(Now));

        var response = await handler.Handle(
            Request(MesContextScanObjectType.DeviceAsset, "device-001") with
            {
                OrganizationId = organizationId,
                EnvironmentId = environmentId,
                WorkOrderId = workOrderId,
                OperationTaskId = operationTaskId,
            },
            CancellationToken.None);

        Assert.Equal(MesContextScanDecision.Rejected, response.Decision);
        Assert.Equal("mes-context-not-found", response.ReasonCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Request_requires_a_resolved_strong_identifier(string scannedObjectId)
    {
        var result = new PrevalidateContextScanQueryValidator().Validate(
            Request(MesContextScanObjectType.DeviceAsset, scannedObjectId));

        Assert.False(result.IsValid);
    }

    private static PrevalidateContextScanQuery Request(
        MesContextScanObjectType objectType,
        string scannedObjectId) =>
        new("org-001", "env-dev", "WO-001", "OP-10", objectType, scannedObjectId);

    private static OperationTask NewTask()
    {
        var task = OperationTask.Queue(
            "org-001", "env-dev", "WO-001", "OP-10", 10, "WC-01", [],
            Now.AddHours(-1), TimeSpan.FromHours(1), operationCode: "OP-CNC", requiredSkillCode: "cnc-operation");
        task.Assign("worker-001", "device-001", null, Now.AddMinutes(-30), assignedUserName: "操作员甲");
        return task;
    }

    private sealed class AcceptingQualificationGate : IMesWorkerSkillQualificationGate
    {
        public static readonly AcceptingQualificationGate Instance = new();

        public Task EnsureQualifiedAsync(
            string organizationId,
            string environmentId,
            string? assignedUserId,
            string? requiredSkillCode,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingQualificationGate : IMesWorkerSkillQualificationGate
    {
        public List<(string OrganizationId, string EnvironmentId, string? UserId, string? SkillCode)> Calls { get; } = [];

        public Task EnsureQualifiedAsync(
            string organizationId,
            string environmentId,
            string? assignedUserId,
            string? requiredSkillCode,
            CancellationToken cancellationToken)
        {
            Calls.Add((organizationId, environmentId, assignedUserId, requiredSkillCode));
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingQualificationGate(string message) : IMesWorkerSkillQualificationGate
    {
        public Task EnsureQualifiedAsync(
            string organizationId,
            string environmentId,
            string? assignedUserId,
            string? requiredSkillCode,
            CancellationToken cancellationToken) =>
            throw new KnownException(message);
    }

    private sealed class CancellingQualificationGate : IMesWorkerSkillQualificationGate
    {
        public CancellationToken CancellationToken { get; private set; }

        public Task EnsureQualifiedAsync(
            string organizationId,
            string environmentId,
            string? assignedUserId,
            string? requiredSkillCode,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
