using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.Errors;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.DistributedLocking;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class QualityNcrDispositionPostgresProfileTests
{
    private const string SharedIdempotencyKey = "quality-rework-shared-001";

    [QualityPostgresFact]
    public async Task Pipeline_scopes_and_persists_rework_disposition_idempotency_per_tenant()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        NonconformanceReportId firstNcrId;
        NonconformanceReportId secondNcrId;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            var first = NewNcr("org-a", "env-a", "NCR-REWORK-PG-A");
            var second = NewNcr("org-b", "env-b", "NCR-REWORK-PG-B");
            db.NonconformanceReports.AddRange(first, second);
            await db.SaveChangesAsync();
            firstNcrId = first.Id;
            secondNcrId = second.Id;
        }

        var reviewedAtUtc = DateTimeOffset.Parse("2026-08-29T10:00:00Z");
        var firstCommand = ReworkCommand(firstNcrId, "org-a", "env-a", reviewedAtUtc);
        await SendInNewScopeAsync(provider, firstCommand);
        await SendInNewScopeAsync(provider, firstCommand);

        var approval = provider.GetRequiredService<RecordingApprovalClient>();
        var automation = provider.GetRequiredService<RecordingCapaAutomationService>();
        Assert.Equal(1, approval.NcrCalls);
        Assert.Equal(1, automation.Calls);

        await Assert.ThrowsAsync<QualityIdempotencyConflictException>(() => SendInNewScopeAsync(
            provider,
            firstCommand with { DispositionApprovalChainId = "approval-chain-002" }));
        await Assert.ThrowsAsync<QualityIdempotencyConflictException>(() => SendInNewScopeAsync(
            provider,
            firstCommand with { AttachmentFileIds = ["evidence-file-002"] }));
        await Assert.ThrowsAsync<QualityIdempotencyConflictException>(() => SendInNewScopeAsync(
            provider,
            firstCommand with
            {
                MrbReviews = [MrbReviewInput.Approve("qa-manager-002", "approved", reviewedAtUtc)],
            }));
        await Assert.ThrowsAsync<QualityLifecycleConflictException>(() => SendInNewScopeAsync(
            provider,
            firstCommand with { IdempotencyKey = "quality-rework-other-001" }));

        var secondCommand = ReworkCommand(secondNcrId, "org-b", "env-b", reviewedAtUtc);
        await SendInNewScopeAsync(provider, secondCommand);

        var tenantMismatch = await Assert.ThrowsAsync<QualityAuthorizationException>(() => SendInNewScopeAsync(
            provider,
            secondCommand with { OrganizationId = "org-a", EnvironmentId = "env-a" }));
        Assert.Equal("ncr-tenant-mismatch", tenantMismatch.Reason);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var first = await db.NonconformanceReports.AsNoTracking().SingleAsync(x => x.Id == firstNcrId);
            var second = await db.NonconformanceReports.AsNoTracking().SingleAsync(x => x.Id == secondNcrId);
            Assert.Equal("disposition-in-progress", first.Status);
            Assert.Equal("disposition-in-progress", second.Status);
            Assert.Equal(2, await db.CodeIdempotencyKeys.CountAsync());
            Assert.Contains(
                await db.CodeIdempotencyKeys.AsNoTracking().ToListAsync(),
                x => x.OrganizationId == "org-a"
                    && x.EnvironmentId == "env-a"
                    && x.IdempotencyKey == SharedIdempotencyKey
                    && x.Code == firstNcrId.ToString());
            Assert.Contains(
                await db.CodeIdempotencyKeys.AsNoTracking().ToListAsync(),
                x => x.OrganizationId == "org-b"
                    && x.EnvironmentId == "env-b"
                    && x.IdempotencyKey == SharedIdempotencyKey
                    && x.Code == secondNcrId.ToString());
        }

        Assert.Equal(2, approval.NcrCalls);
        Assert.Equal(2, automation.Calls);
        var distributedLock = provider.GetRequiredService<RecordingDistributedLock>();
        Assert.Contains(
            $"business-quality:ncr-disposition:org-a:env-a:{firstNcrId}",
            distributedLock.AcquiredKeys);
        Assert.Contains(
            $"business-quality:ncr-disposition:org-b:env-b:{secondNcrId}",
            distributedLock.AcquiredKeys);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddValidatorsFromAssembly(typeof(Program).Assembly);
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly)
                .AddOpenBehavior(typeof(NervIipCommandLockBehavior<,>))
                .AddKnownExceptionValidationBehavior()
                .AddUnitOfWorkBehaviors());
        services.AddScoped<
            ICommandLock<SubmitNonconformanceReportDispositionCommand>,
            SubmitNonconformanceReportDispositionCommandLock>();
        services.AddQualityPostgreSqlPersistence(QualityPostgresLaneDatabase.ConnectionString);
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<IQualityIntegrationEventContextAccessor, FixedIntegrationEventContextAccessor>();
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());
        services.AddSingleton<RecordingApprovalClient>();
        services.AddSingleton<IApprovalChainStatusClient>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingApprovalClient>());
        services.AddSingleton<RecordingCapaAutomationService>();
        services.AddSingleton<ICapaAutomationService>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingCapaAutomationService>());
        services.AddSingleton<RecordingDistributedLock>();
        services.AddSingleton<IDistributedLock>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingDistributedLock>());
        return services.BuildServiceProvider();
    }

    private static async Task SendInNewScopeAsync(
        IServiceProvider provider,
        SubmitNonconformanceReportDispositionCommand command)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, CancellationToken.None);
    }

    private static SubmitNonconformanceReportDispositionCommand ReworkCommand(
        NonconformanceReportId ncrId,
        string organizationId,
        string environmentId,
        DateTimeOffset reviewedAtUtc) =>
        new(
            ncrId,
            organizationId,
            environmentId,
            "rework",
            "approval-chain-001",
            ["evidence-file-001"],
            [MrbReviewInput.Approve("qa-manager-001", "approved", reviewedAtUtc)],
            SharedIdempotencyKey);

    private static NonconformanceReport NewNcr(string organizationId, string environmentId, string ncrCode) =>
        NonconformanceReport.Open(
            organizationId,
            environmentId,
            ncrCode,
            "receiving",
            $"RCV-{ncrCode}",
            "SKU-RM-1000",
            1m,
            "dimension-out-of-spec",
            null,
            null,
            []);

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
            cancellationToken.ThrowIfCancellationRequested();
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

        public Task OpenForDispositionIfRequiredAsync(
            NonconformanceReport ncr,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDistributedLock : IDistributedLock
    {
        public List<string> AcquiredKeys { get; } = [];

        public ILockSynchronizationHandler? TryAcquire(
            string key,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            TryAcquireAsync(key, timeout, cancellationToken).AsTask().GetAwaiter().GetResult();

        public ILockSynchronizationHandler Acquire(
            string key,
            TimeSpan? timeout,
            CancellationToken cancellationToken) =>
            AcquireAsync(key, timeout, cancellationToken).AsTask().GetAwaiter().GetResult();

        public ValueTask<ILockSynchronizationHandler?> TryAcquireAsync(
            string key,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquiredKeys.Add(key);
            return ValueTask.FromResult<ILockSynchronizationHandler?>(new RecordingLockHandle());
        }

        public async ValueTask<ILockSynchronizationHandler> AcquireAsync(
            string key,
            TimeSpan? timeout,
            CancellationToken cancellationToken) =>
            await TryAcquireAsync(key, timeout ?? TimeSpan.FromSeconds(30), cancellationToken)
                ?? throw new TimeoutException($"Could not acquire {key}.");

        private sealed class RecordingLockHandle : ILockSynchronizationHandler
        {
            public CancellationToken HandleLostToken => CancellationToken.None;

            public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FixedIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext() =>
            new("correlation-ncr-rework-pg", "causation-ncr-rework-pg", "user:qa-manager-001");
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
