using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.Errors;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityLifecycleConflictTests
{
    [Fact]
    public void Persistence_backstop_only_classifies_quality_idempotency_constraints()
    {
        using var dbContext = CreateDbContext();

        Assert.True(QualityIdempotencyPersistenceConflicts.IsTargetConflict(
            UniqueConflict("ux_code_idempotency_keys_scope"),
            dbContext));
        Assert.True(QualityIdempotencyPersistenceConflicts.IsTargetConflict(
            UniqueConflict("ux_inspection_task_assignment_receipts_key"),
            dbContext));
        Assert.False(QualityIdempotencyPersistenceConflicts.IsTargetConflict(
            UniqueConflict("ux_unrelated_quality_constraint"),
            dbContext));
    }

    [Fact]
    public async Task In_progress_task_without_plan_remains_a_known_validation_failure()
    {
        await using var dbContext = CreateDbContext();
        var task = NewPendingTask("DOC-CONFLICT");
        task.Start("inspector-001", DateTimeOffset.Parse("2026-07-27T08:00:00Z"));
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            CreateTaskHandler(dbContext).Handle(
                new CreateInspectionRecordFromTaskCommand(task.Id, "inspector-001", [], null, [], "lifecycle-submit-1", "org-001", "env-dev"),
                CancellationToken.None));

        Assert.Contains("plan", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<QualityLifecycleConflictException>(exception);
        Assert.Empty(dbContext.InspectionRecords);
    }

    [Fact]
    public async Task Existing_source_record_is_a_legal_replay_for_in_progress_task()
    {
        await using var dbContext = CreateDbContext();
        var task = NewPendingTask("DOC-EXISTING-STARTED");
        task.Start("inspector-001", DateTimeOffset.Parse("2026-07-27T08:00:00Z"));

        var record = NewInspectionRecord(task.SourceDocumentId);
        dbContext.InspectionTasks.Add(task);
        dbContext.InspectionRecords.Add(record);
        await dbContext.SaveChangesAsync();

        var result = await CreateTaskHandler(dbContext).Handle(
            new CreateInspectionRecordFromTaskCommand(task.Id, "inspector-001", [], null, [], "lifecycle-submit-2", "org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal(record.Id, result.InspectionRecordId);
        Assert.Equal(InspectionTaskStatuses.Completed, task.Status);
        Assert.Equal(record.Id, task.InspectionRecordId);
        Assert.Single(dbContext.InspectionRecords);
    }

    [Fact]
    public async Task Completed_task_with_linked_record_remains_a_legal_replay()
    {
        await using var dbContext = CreateDbContext();
        var task = NewPendingTask("DOC-COMPLETED");
        var record = NewInspectionRecord(task.SourceDocumentId);
        task.Start("inspector-001", DateTimeOffset.Parse("2026-07-27T08:00:00Z"));
        task.Complete(record.Id, DateTimeOffset.Parse("2026-07-27T08:05:00Z"));
        dbContext.InspectionTasks.Add(task);
        dbContext.InspectionRecords.Add(record);
        await dbContext.SaveChangesAsync();

        var result = await CreateTaskHandler(dbContext).Handle(
            new CreateInspectionRecordFromTaskCommand(task.Id, "inspector-001", [], null, [], "lifecycle-submit-3", "org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal(record.Id, result.InspectionRecordId);
        Assert.Equal(InspectionTaskStatuses.Completed, task.Status);
        Assert.Single(dbContext.InspectionRecords);
    }

    [Theory]
    [InlineData(InspectionTaskStatuses.Pending)]
    [InlineData(InspectionTaskStatuses.Completed)]
    [InlineData("unexpected-status")]
    public async Task Invalid_task_phase_rejects_existing_source_record_without_mutating_either_entity(string status)
    {
        await using var dbContext = CreateDbContext();
        var task = NewPendingTask($"DOC-INVALID-{status}");
        task.Assign("inspector-001", null, task.Version, DateTimeOffset.Parse("2026-07-27T07:30:00Z"));
        typeof(InspectionTask)
            .GetProperty(nameof(InspectionTask.Status))!
            .SetValue(task, status);
        var record = NewInspectionRecord(task.SourceDocumentId);
        dbContext.InspectionTasks.Add(task);
        dbContext.InspectionRecords.Add(record);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<QualityLifecycleConflictException>(() =>
            CreateTaskHandler(dbContext).Handle(
                new CreateInspectionRecordFromTaskCommand(task.Id, "inspector-001", [], null, [], "lifecycle-submit-4", "org-001", "env-dev"),
                CancellationToken.None));

        Assert.Equal("create-inspection-record-from-task", exception.Action);
        Assert.Equal(status, exception.CurrentStatus);
        Assert.Equal(status, task.Status);
        Assert.Null(task.InspectionRecordId);
        Assert.Single(dbContext.InspectionRecords);
        Assert.Equal(record.Id, dbContext.InspectionRecords.Single().Id);
    }

    [Fact]
    public async Task Pending_task_with_missing_plan_remains_a_known_validation_failure()
    {
        await using var dbContext = CreateDbContext();
        var task = NewPendingTask("DOC-MISSING-PLAN");
        task.Start("inspector-001", DateTimeOffset.Parse("2026-07-27T08:00:00Z"));
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            CreateTaskHandler(dbContext).Handle(
                new CreateInspectionRecordFromTaskCommand(task.Id, "inspector-001", [], null, [], "lifecycle-submit-5", "org-001", "env-dev"),
                CancellationToken.None));

        Assert.Contains("plan", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<QualityLifecycleConflictException>(exception);
    }

    [Fact]
    public async Task Submit_rejects_an_inspection_task_owned_by_another_business_scope_without_mutation()
    {
        await using var dbContext = CreateDbContext();
        var task = NewPendingTask("DOC-TENANT-B");
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<QualityAuthorizationException>(() =>
            CreateTaskHandler(dbContext).Handle(
                new CreateInspectionRecordFromTaskCommand(
                    task.Id,
                    "inspector-a",
                    [],
                    null,
                    [],
                    "tenant-a-attempt",
                    "org-a",
                    "env-a"),
                CancellationToken.None));

        Assert.Equal("task-tenant-mismatch", exception.Reason);
        Assert.Equal(InspectionTaskStatuses.Pending, task.Status);
        Assert.Null(task.InspectionRecordId);
        Assert.Empty(dbContext.InspectionRecords);
    }

    [Theory]
    [InlineData("disposition-in-progress")]
    [InlineData("closed")]
    public async Task Submit_disposition_rejects_non_open_ncr_before_approval_or_automation(string status)
    {
        await using var dbContext = CreateDbContext();
        var ncr = NewNcr($"NCR-SUBMIT-{status}");
        MoveNcrToStatus(ncr, status);
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync();
        var approval = new RecordingApprovalClient();
        var automation = new RecordingCapaAutomationService();
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            approval,
            automation);

        var exception = await Assert.ThrowsAsync<QualityLifecycleConflictException>(() =>
            handler.Handle(
                new SubmitNonconformanceReportDispositionCommand(
                    ncr.Id,
                    "scrap",
                    "approval-chain-001",
                    [],
                    [MrbReviewInput.Approve("qa-manager-001", "approved", DateTimeOffset.Parse("2026-07-27T08:00:00Z"))]),
                CancellationToken.None));

        Assert.Equal("submit-ncr-disposition", exception.Action);
        Assert.Equal(status, exception.CurrentStatus);
        Assert.Equal(0, approval.NcrCalls);
        Assert.Equal(0, automation.Calls);
    }

    [Fact]
    public async Task Submit_disposition_with_missing_approval_remains_a_known_validation_failure()
    {
        await using var dbContext = CreateDbContext();
        var ncr = NewNcr("NCR-SUBMIT-APPROVAL");
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync();
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new RecordingApprovalClient(),
            new RecordingCapaAutomationService());

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(
                new SubmitNonconformanceReportDispositionCommand(ncr.Id, "scrap", null, [], []),
                CancellationToken.None));

        Assert.Contains("approval", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<QualityLifecycleConflictException>(exception);
    }

    [Fact]
    public async Task Submit_disposition_with_missing_evidence_remains_a_known_validation_failure()
    {
        await using var dbContext = CreateDbContext();
        var ncr = NewNcr("NCR-SUBMIT-EVIDENCE");
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync();
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new RecordingApprovalClient(),
            new RecordingCapaAutomationService());

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(
                new SubmitNonconformanceReportDispositionCommand(ncr.Id, "sort-and-screen", null, [], []),
                CancellationToken.None));

        Assert.Contains("evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<QualityLifecycleConflictException>(exception);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("closed")]
    public async Task Close_rejects_ncr_outside_disposition_in_progress_before_capa_readiness(string status)
    {
        await using var dbContext = CreateDbContext();
        var ncr = NewNcr($"NCR-CLOSE-{status}", "customer-return");
        if (status == "closed")
        {
            MoveNcrToStatus(ncr, status);
        }

        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync();
        var handler = new CloseNonconformanceReportCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new CorrectiveActionRepository(dbContext),
            new FixedIntegrationEventContextAccessor());

        var exception = await Assert.ThrowsAsync<QualityLifecycleConflictException>(() =>
            handler.Handle(
                new CloseNonconformanceReportCommand(ncr.Id, null, null, null, "done"),
                CancellationToken.None));

        Assert.Equal("close-ncr", exception.Action);
        Assert.Equal(status, exception.CurrentStatus);
    }

    [Fact]
    public async Task Close_with_matching_phase_but_missing_capa_remains_a_known_validation_failure()
    {
        await using var dbContext = CreateDbContext();
        var ncr = NewNcr("NCR-CLOSE-CAPA", "customer-return");
        MoveNcrToStatus(ncr, "disposition-in-progress");
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync();
        var handler = new CloseNonconformanceReportCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new CorrectiveActionRepository(dbContext),
            new FixedIntegrationEventContextAccessor());

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(
                new CloseNonconformanceReportCommand(ncr.Id, null, null, null, "done"),
                CancellationToken.None));

        Assert.Contains("CAPA", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<QualityLifecycleConflictException>(exception);
    }

    [Fact]
    public async Task Close_with_matching_status_but_missing_disposition_fact_is_a_lifecycle_conflict()
    {
        await using var dbContext = CreateDbContext();
        var ncr = NewNcr("NCR-CLOSE-MISSING-DISPOSITION");
        typeof(NonconformanceReport)
            .GetProperty(nameof(NonconformanceReport.Status))!
            .SetValue(ncr, "disposition-in-progress");
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync();
        var handler = new CloseNonconformanceReportCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new CorrectiveActionRepository(dbContext),
            new FixedIntegrationEventContextAccessor());

        var exception = await Assert.ThrowsAsync<QualityLifecycleConflictException>(() =>
            handler.Handle(
                new CloseNonconformanceReportCommand(ncr.Id, null, null, null, "done"),
                CancellationToken.None));

        Assert.Equal("close-ncr", exception.Action);
        Assert.Equal("disposition-in-progress", exception.CurrentStatus);
    }

    [Fact]
    public async Task Lifecycle_conflict_endpoint_returns_409_with_safe_code()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_lifecycle;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                    }));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new LifecycleConflictSender());
                });
            });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-internal-service-token");
        var taskId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/api/business/v1/quality/inspection-tasks/{taskId}/inspection-record",
            new
            {
                inspectionTaskId = taskId,
                organizationId = "org-001",
                environmentId = "env-dev",
                inspectorUserId = "inspector-001",
                resultLines = Array.Empty<object>(),
                idempotencyKey = "quality-lifecycle-http",
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"message\":\"lifecycle-conflict\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(InspectionTaskStatuses.InProgress, body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("authorization", 403, "task-outside-selected-work-scope")]
    [InlineData("lifecycle", 409, "lifecycle-conflict")]
    [InlineData("already-claimed", 422, "task-already-claimed")]
    public async Task Claim_endpoint_preserves_authorization_lifecycle_and_already_claimed_statuses(
        string failureKind,
        int expectedStatusCode,
        string expectedSafeCode)
    {
        Exception exception = failureKind switch
        {
            "authorization" => QualityAuthorizationException.Forbidden("task-outside-selected-work-scope"),
            "lifecycle" => new QualityLifecycleConflictException("claim", InspectionTaskStatuses.Completed),
            _ => new QualityUnprocessableException("task-already-claimed"),
        };
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_claim;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                    }));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new ExceptionSender(exception));
                });
            });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-internal-service-token");
        var taskId = Guid.CreateVersion7();

        var response = await client.PostAsJsonAsync(
            $"/api/business/v1/quality/inspection-tasks/{taskId}/claim",
            new
            {
                inspectionTaskId = taskId,
                organizationId = "org-001",
                environmentId = "env-dev",
                actorPrincipalId = "inspector-002",
                authorizedTeamIds = new[] { "TEAM-QA" },
                idempotencyKey = "quality-claim-second",
                expectedVersion = 3,
            });

        Assert.Equal(expectedStatusCode, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains($"\"message\":\"{expectedSafeCode}\"", body, StringComparison.OrdinalIgnoreCase);
    }

    private static CreateInspectionRecordFromTaskCommandHandler CreateTaskHandler(ApplicationDbContext dbContext)
    {
        return new CreateInspectionRecordFromTaskCommandHandler(
            new InspectionTaskRepository(dbContext),
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new NonconformanceReportRepository(dbContext),
            new NonconformanceReportCodeGenerator());
    }

    private static InspectionTask NewPendingTask(string sourceDocumentId)
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            new Domain.AggregatesModel.InspectionPlanAggregate.InspectionPlanId(Guid.CreateVersion7()),
            "receiving",
            "wms",
            sourceDocumentId,
            "LINE-001",
            "SKU-RM-1000",
            10m,
            "kg",
            null,
            null,
            DateTimeOffset.Parse("2026-07-27T07:00:00Z"),
            DateTimeOffset.Parse("2026-07-28T07:00:00Z"),
            $"quality-task:{sourceDocumentId}");
    }

    private static InspectionRecord NewInspectionRecord(string sourceDocumentId)
    {
        return InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "wms",
            sourceDocumentId,
            "SKU-RM-1000",
            10m,
            null,
            null,
            [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
            null,
            []);
    }

    private static NonconformanceReport NewNcr(string ncrCode, string sourceType = "receiving")
    {
        return NonconformanceReport.Open(
            "org-001",
            "env-dev",
            ncrCode,
            sourceType,
            $"DOC-{ncrCode}",
            "SKU-RM-1000",
            1m,
            "defect",
            null,
            null,
            []);
    }

    private static void MoveNcrToStatus(NonconformanceReport ncr, string status)
    {
        ncr.SubmitDisposition("sort-and-screen", null, ["evidence-file"]);
        if (status == "closed")
        {
            ncr.Close(null, null, null, "done", "user:qa-manager-001");
        }
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"quality-lifecycle-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static DbUpdateException UniqueConflict(string constraintName) =>
        new("unique conflict", new FakePostgresException("23505", constraintName));

    private sealed class FakePostgresException(string sqlState, string constraintName) : Exception
    {
        public string SqlState { get; } = sqlState;

        public string ConstraintName { get; } = constraintName;
    }

    private sealed class RecordingApprovalClient : IApprovalChainStatusClient
    {
        public int NcrCalls { get; private set; }

        public Task<bool> IsApprovedForNcrDispositionAsync(
            string chainId,
            string organizationId,
            string environmentId,
            string ncrCode,
            CancellationToken cancellationToken)
        {
            NcrCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> IsApprovedForCapaClosureAsync(
            string chainId,
            string organizationId,
            string environmentId,
            string capaCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class RecordingCapaAutomationService : ICapaAutomationService
    {
        public int Calls { get; private set; }

        public Task OpenForDispositionIfRequiredAsync(NonconformanceReport ncr, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext() =>
            new("correlation-001", "causation-001", "user:qa-manager-001");
    }

    private sealed class LifecycleConflictSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromException<TResponse>(
                new QualityLifecycleConflictException(
                    "create-inspection-record-from-task",
                    InspectionTaskStatuses.InProgress));
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ExceptionSender(Exception exception) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<TResponse>(exception);

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
