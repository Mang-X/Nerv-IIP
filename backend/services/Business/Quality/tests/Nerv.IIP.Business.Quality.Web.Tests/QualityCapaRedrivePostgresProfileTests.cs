using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityCapaRedrivePostgresProfileTests
{
    [QualityPostgresFact]
    public async Task Postgres_uow_persists_recorded_scrap_ncr_redrive_after_capa_effectiveness()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly)
                .AddUnitOfWorkBehaviors());
        services.AddQualityPostgreSqlPersistence(connectionString);
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<IQualityIntegrationEventContextAccessor, FixedQualityIntegrationEventContextAccessor>();
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<RecordingIntegrationEventPublisher>();
        NonconformanceReportId ncrId;
        CorrectiveActionId capaId;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DropQualitySchemaAsync(db);
            await db.Database.MigrateAsync();

            var ncr = NewRecordedScrapNcr("NCR-SCRAP-CAPA-PG-001", "RCV-SCRAP-CAPA-PG-001");
            var capa = NewCompletedOpenCapa(ncr, "CAPA-SCRAP-PG-001");
            db.NonconformanceReports.Add(ncr);
            db.CorrectiveActions.Add(capa);
            await db.SaveChangesAsync();
            ncrId = ncr.Id;
            capaId = capa.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(
                new VerifyCorrectiveActionEffectivenessCommand(
                    capaId,
                    "qa-manager-001",
                    "No recurrence",
                    DateTimeOffset.Parse("2026-07-10T00:00:00Z")),
                CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reloadedNcr = await db.NonconformanceReports.SingleAsync(x => x.Id == ncrId);
            Assert.Equal("closed", reloadedNcr.Status);
            Assert.Equal("SM-FULL-CAPA-PG-001", reloadedNcr.ScrapMovementId);
        }

        Assert.IsType<NcrClosedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.DoesNotContain(publisher.Published, x => x is InventoryMovementRequestedIntegrationEvent);
    }

    private static NonconformanceReport NewRecordedScrapNcr(string ncrCode, string sourceDocumentId)
    {
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            ncrCode,
            "receiving",
            sourceDocumentId,
            "SKU-RM-1000",
            10m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        ncr.SubmitDisposition(
            "scrap",
            "approval-chain-approved",
            [],
            [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]);
        ncr.RecordScrapDispositionMovement("SM-FULL-CAPA-PG-001", -10m);
        return ncr;
    }

    private static CorrectiveAction NewCompletedOpenCapa(NonconformanceReport ncr, string capaCode)
    {
        var capa = CorrectiveAction.OpenFromNcr(
            ncr.OrganizationId,
            ncr.EnvironmentId,
            capaCode,
            ncr,
            "Root cause confirmed",
            "Contain affected material",
            "qa-engineer-001",
            DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        capa.AddAction("corrective", "Fix supplier process", "supplier-quality-001", DateTimeOffset.Parse("2026-06-20T00:00:00Z"));
        var action = capa.Actions.Single();
        capa.CompleteAction(action.Id, action.OwnerUserId, DateTimeOffset.Parse("2026-06-21T00:00:00Z"));
        return capa;
    }

    private static async Task DropQualitySchemaAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{QualityFacts.Schema}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext()
        {
            return new QualityIntegrationEventContext(
                "corr-capa-redrive-pg-001",
                "cause-capa-redrive-pg-001",
                "system:business-quality");
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class QualityPostgresFactAttribute : FactAttribute
{
    public QualityPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run Quality PostgreSQL profile tests.";
        }
    }
}
