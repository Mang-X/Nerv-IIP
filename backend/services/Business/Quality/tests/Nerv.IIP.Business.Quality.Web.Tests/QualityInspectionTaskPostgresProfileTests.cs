using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityInspectionTaskPostgresProfileTests
{
    [QualityPostgresFact]
    public async Task Postgres_duplicate_retry_persists_non_conflicting_tasks_after_unique_conflict()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
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
}
