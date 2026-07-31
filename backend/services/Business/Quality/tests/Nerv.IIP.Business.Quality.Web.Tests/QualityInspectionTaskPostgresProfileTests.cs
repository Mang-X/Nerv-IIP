using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Errors;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityInspectionTaskPostgresProfileTests
{
    [QualityPostgresFact]
    public async Task Postgres_second_claim_is_unprocessable_and_stale_concurrent_claim_is_rejected()
    {
        await using var database = await QualityPostgresTestDatabase.CreateAsync(
            nameof(Postgres_second_claim_is_unprocessable_and_stale_concurrent_claim_is_rejected));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        InspectionTaskId firstTaskId;
        InspectionTaskId concurrentTaskId;

        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setup.Database.MigrateAsync();
            var first = NewTask(ActivePlan().Id, "RCV-CLAIM-ONE", "LINE-001", "SKU-RM-1000", "pg:claim:one");
            var concurrent = NewTask(ActivePlan().Id, "RCV-CLAIM-TWO", "LINE-001", "SKU-RM-1000", "pg:claim:two");
            first.Assign(null, "TEAM-QA", first.Version, DateTimeOffset.Parse("2026-07-30T08:00:00Z"));
            concurrent.Assign(null, "TEAM-QA", concurrent.Version, DateTimeOffset.Parse("2026-07-30T08:00:00Z"));
            setup.InspectionTasks.AddRange(first, concurrent);
            await setup.SaveChangesAsync();
            firstTaskId = first.Id;
            concurrentTaskId = concurrent.Id;
        }

        await using (var winner = new ApplicationDbContext(options, new NoopMediator()))
        {
            await new ClaimInspectionTaskCommandHandler(winner).Handle(
                new ClaimInspectionTaskCommand(
                    firstTaskId, "org-001", "env-dev", "qa-user-001", ["TEAM-QA"], "claim-winner", 2),
                CancellationToken.None);
            await winner.SaveChangesAsync();
        }

        await using (var retry = new ApplicationDbContext(options, new NoopMediator()))
        {
            var exception = await Assert.ThrowsAsync<QualityUnprocessableException>(() =>
                new ClaimInspectionTaskCommandHandler(retry).Handle(
                    new ClaimInspectionTaskCommand(
                        firstTaskId, "org-001", "env-dev", "qa-user-002", ["TEAM-QA"], "claim-retry", 3),
                    CancellationToken.None));
            Assert.Equal("task-already-claimed", exception.Reason);
        }

        await using var firstContext = new ApplicationDbContext(options, new NoopMediator());
        await using var secondContext = new ApplicationDbContext(options, new NoopMediator());
        var firstCandidate = await firstContext.InspectionTasks.SingleAsync(x => x.Id == concurrentTaskId);
        var secondCandidate = await secondContext.InspectionTasks.SingleAsync(x => x.Id == concurrentTaskId);
        firstCandidate.Claim("qa-user-001", ["TEAM-QA"], 2, DateTimeOffset.Parse("2026-07-30T08:10:00Z"));
        secondCandidate.Claim("qa-user-002", ["TEAM-QA"], 2, DateTimeOffset.Parse("2026-07-30T08:10:01Z"));
        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    [QualityPostgresFact]
    public async Task Assignment_scope_migration_backfills_existing_task_version_to_one()
    {
        await using var database = await QualityPostgresTestDatabase.CreateAsync(
            nameof(Assignment_scope_migration_backfills_existing_task_version_to_one));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        var taskId = Guid.CreateVersion7();

        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260724073658_AddQualityReinspectionHistory");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO quality.inspection_tasks (
                id, organization_id, environment_id, inspection_plan_id,
                source_type, source_service, source_document_id, source_document_line_id,
                sku_code, quantity, uom_code, status, created_at_utc, updated_at_utc,
                due_at_utc, trigger_idempotency_key)
            VALUES (
                {taskId}, 'org-001', 'env-dev', {Guid.CreateVersion7()},
                'receiving', 'wms', 'RCV-UPGRADE-001', 'LINE-001',
                'SKU-RM-1000', 10, 'kg', 'pending',
                TIMESTAMPTZ '2026-07-30T08:00:00Z', TIMESTAMPTZ '2026-07-30T08:00:00Z',
                TIMESTAMPTZ '2026-07-31T08:00:00Z', 'upgrade-existing-task-001')
            """);

        await migrator.MigrateAsync();

        var version = await db.Database
            .SqlQuery<long>($"""SELECT version AS "Value" FROM quality.inspection_tasks WHERE id = {taskId}""")
            .SingleAsync();
        Assert.Equal(1, version);
    }

    [QualityPostgresFact]
    public async Task Postgres_persists_assignment_claim_and_audit_receipts()
    {
        await using var database = await QualityPostgresTestDatabase.CreateAsync(
            nameof(Postgres_persists_assignment_claim_and_audit_receipts));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        var taskId = default(InspectionTaskId);

        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            await db.Database.MigrateAsync();
            var task = NewTask(
                ActivePlan().Id,
                "RCV-ASSIGNMENT",
                "LINE-001",
                "SKU-RM-1000",
                "wms:assignment:001");
            task.Assign(null, "team-quality-a", task.Version, DateTimeOffset.Parse("2026-07-30T08:00:00Z"));
            db.InspectionTasks.Add(task);
            db.InspectionTaskAssignmentReceipts.Add(InspectionTaskAssignmentReceipt.Create(
                task.OrganizationId,
                task.EnvironmentId,
                task.Id,
                "assign",
                "assign-pg-001",
                "assign-fingerprint",
                "manager-001",
                null,
                null,
                null,
                "team-quality-a",
                "shift assignment",
                task.Version,
                DateTimeOffset.Parse("2026-07-30T08:00:00Z")));
            await db.SaveChangesAsync();
            taskId = task.Id;
        }

        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            var task = await db.InspectionTasks.SingleAsync(x => x.Id == taskId);
            task.Claim(
                "inspector-001",
                ["team-quality-a"],
                task.Version,
                DateTimeOffset.Parse("2026-07-30T08:05:00Z"));
            db.InspectionTaskAssignmentReceipts.Add(InspectionTaskAssignmentReceipt.Create(
                task.OrganizationId,
                task.EnvironmentId,
                task.Id,
                "claim",
                "claim-pg-001",
                "claim-fingerprint",
                "inspector-001",
                null,
                "team-quality-a",
                "inspector-001",
                "team-quality-a",
                null,
                task.Version,
                DateTimeOffset.Parse("2026-07-30T08:05:00Z")));
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options, new NoopMediator()))
        {
            var task = await db.InspectionTasks.AsNoTracking().SingleAsync(x => x.Id == taskId);
            var receipts = await db.InspectionTaskAssignmentReceipts
                .AsNoTracking()
                .Where(x => x.InspectionTaskId == taskId)
                .OrderBy(x => x.CreatedAtUtc)
                .ToArrayAsync();

            Assert.Equal(InspectionTaskStatuses.InProgress, task.Status);
            Assert.Equal("inspector-001", task.AssignedUserId);
            Assert.Equal("team-quality-a", task.AssignedTeamId);
            Assert.Equal(3, task.Version);
            Assert.Collection(
                receipts,
                assignment =>
                {
                    Assert.Equal("assign", assignment.Action);
                    Assert.Equal(2, assignment.ResultVersion);
                },
                claim =>
                {
                    Assert.Equal("claim", claim.Action);
                    Assert.Equal(3, claim.ResultVersion);
                });
        }
    }

    [QualityPostgresFact]
    public async Task Postgres_duplicate_retry_persists_non_conflicting_tasks_after_unique_conflict()
    {
        await using var database = await QualityPostgresTestDatabase.CreateAsync(
            nameof(Postgres_duplicate_retry_persists_non_conflicting_tasks_after_unique_conflict));
        var connectionString = database.ConnectionString;
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddQualityPostgreSqlPersistence(connectionString);

        await using var provider = services.BuildServiceProvider();
        InspectionPlanId planId;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DropQualitySchemaAsync(db);
            await db.Database.MigrateAsync();

            var plan = ActivePlan();
            db.InspectionPlans.Add(plan);
            db.InspectionTasks.Add(NewTask(plan.Id, "RCV-CONCURRENT", "LINE-DUP", "SKU-RM-1000", "wms:concurrent:duplicate"));
            await db.SaveChangesAsync();
            planId = plan.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.InspectionTasks.Add(NewTask(planId, "RCV-CONCURRENT", "LINE-DUP", "SKU-RM-1000", "wms:concurrent:duplicate"));
            db.InspectionTasks.Add(NewTask(planId, "RCV-CONCURRENT", "LINE-NEW", "SKU-RM-1000", "wms:concurrent:new"));

            await InvokeSaveChangesIgnoreDuplicateTasksAsync(db);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tasks = await db.InspectionTasks
                .Where(x => x.SourceDocumentId == "RCV-CONCURRENT")
                .OrderBy(x => x.SourceDocumentLineId)
                .ToArrayAsync();

            Assert.Collection(
                tasks,
                duplicate => Assert.Equal("LINE-DUP", duplicate.SourceDocumentLineId),
                persisted => Assert.Equal("LINE-NEW", persisted.SourceDocumentLineId));
        }
    }

    private static async Task InvokeSaveChangesIgnoreDuplicateTasksAsync(ApplicationDbContext dbContext)
    {
        var generationType = typeof(WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks)
            .Assembly
            .GetType("Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers.InspectionTaskGeneration", throwOnError: true)!;
        var method = generationType.GetMethod("SaveChangesIgnoreDuplicateTasksAsync", BindingFlags.Public | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, [dbContext, CancellationToken.None])!;
        await task;
    }

    private static InspectionPlan ActivePlan()
    {
        var plan = InspectionPlan.Create("org-001", "env-dev", "PLAN-RCV-PG-1000", "receiving", "SKU-RM-1000", null, null, null, null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static InspectionTask NewTask(
        InspectionPlanId planId,
        string sourceDocumentId,
        string sourceDocumentLineId,
        string skuCode,
        string triggerIdempotencyKey)
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            planId,
            "receiving",
            "wms",
            sourceDocumentId,
            sourceDocumentLineId,
            skuCode,
            10m,
            "kg",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            triggerIdempotencyKey);
    }

    private static async Task DropQualitySchemaAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{QualityFacts.Schema}\" CASCADE";
        await command.ExecuteNonQueryAsync();
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
