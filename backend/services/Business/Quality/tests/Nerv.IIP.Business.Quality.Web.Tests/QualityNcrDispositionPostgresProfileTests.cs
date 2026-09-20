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
        var lockStore = new ObservedRedisCommandLockStore(new InMemoryRedisCommandLockStore(TimeProvider.System));
        var approval = new ControlledApprovalClient();
        var automation = new RecordingCapaAutomationService();
        var integrationEvents = new RecordingIntegrationEventPublisher();
        await using var firstProvider = CreateProvider(lockStore, approval, automation, integrationEvents);
        await using var secondProvider = CreateProvider(lockStore, approval, automation, integrationEvents);
        NonconformanceReportId firstNcrId;
        NonconformanceReportId secondNcrId;

        using (var scope = firstProvider.CreateScope())
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
        var firstSend = SendInNewScopeAsync(firstProvider, firstCommand);
        await approval.FirstCallEntered.WaitAsync(TimeSpan.FromSeconds(5));
        var secondSend = SendInNewScopeAsync(secondProvider, firstCommand);
        try
        {
            var competingAttempt = await lockStore.SecondAttempt.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal($"business-quality:ncr-disposition:org-a:env-a:{firstNcrId}", competingAttempt.Key);
            Assert.False(competingAttempt.Acquired);
            Assert.False(secondSend.IsCompleted);
            Assert.Equal(1, approval.NcrCalls);
        }
        finally
        {
            approval.ReleaseFirstCall();
        }

        await Task.WhenAll(firstSend, secondSend);

        Assert.Equal(1, approval.NcrCalls);
        Assert.Equal(1, automation.Calls);
        Assert.Equal(1, integrationEvents.ReworkRequestedCalls);

        await Assert.ThrowsAsync<QualityIdempotencyConflictException>(() => SendInNewScopeAsync(
            firstProvider,
            firstCommand with { DispositionApprovalChainId = "approval-chain-002" }));
        await Assert.ThrowsAsync<QualityIdempotencyConflictException>(() => SendInNewScopeAsync(
            firstProvider,
            firstCommand with { AttachmentFileIds = ["evidence-file-002"] }));
        await Assert.ThrowsAsync<QualityIdempotencyConflictException>(() => SendInNewScopeAsync(
            firstProvider,
            firstCommand with
            {
                MrbReviews = [MrbReviewInput.Approve("qa-manager-002", "approved", reviewedAtUtc)],
            }));
        await Assert.ThrowsAsync<QualityLifecycleConflictException>(() => SendInNewScopeAsync(
            firstProvider,
            firstCommand with { IdempotencyKey = "quality-rework-other-001" }));

        var secondCommand = ReworkCommand(secondNcrId, "org-b", "env-b", reviewedAtUtc);
        await SendInNewScopeAsync(secondProvider, secondCommand);

        var tenantMismatch = await Assert.ThrowsAsync<QualityAuthorizationException>(() => SendInNewScopeAsync(
            firstProvider,
            secondCommand with { OrganizationId = "org-a", EnvironmentId = "env-a" }));
        Assert.Equal("ncr-tenant-mismatch", tenantMismatch.Reason);

        using (var scope = firstProvider.CreateScope())
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
        Assert.Equal(2, integrationEvents.ReworkRequestedCalls);
        Assert.Contains(
            $"business-quality:ncr-disposition:org-a:env-a:{firstNcrId}",
            lockStore.AcquiredKeys);
        Assert.Contains(
            $"business-quality:ncr-disposition:org-b:env-b:{secondNcrId}",
            lockStore.AcquiredKeys);
    }

    private static ServiceProvider CreateProvider(
        ObservedRedisCommandLockStore lockStore,
        ControlledApprovalClient approval,
        RecordingCapaAutomationService automation,
        RecordingIntegrationEventPublisher integrationEvents)
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
        services.AddSingleton(integrationEvents);
        services.AddSingleton<IIntegrationEventPublisher>(integrationEvents);
        services.AddSingleton(approval);
        services.AddSingleton<IApprovalChainStatusClient>(approval);
        services.AddSingleton(automation);
        services.AddSingleton<ICapaAutomationService>(automation);
        services.AddSingleton<IDistributedLock>(new RedisCommandDistributedLock(lockStore, TimeProvider.System));
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

    private sealed class ControlledApprovalClient : IApprovalChainStatusClient
    {
        private readonly TaskCompletionSource firstCallEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int ncrCalls;

        public Task FirstCallEntered => firstCallEntered.Task;

        public int NcrCalls => Volatile.Read(ref ncrCalls);

        public void ReleaseFirstCall() => releaseFirstCall.TrySetResult();

        public async Task<bool> IsApprovedForNcrDispositionAsync(
            string chainId,
            string organizationId,
            string environmentId,
            string ncrCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref ncrCalls) == 1)
            {
                firstCallEntered.TrySetResult();
                await releaseFirstCall.Task.WaitAsync(cancellationToken);
            }

            return true;
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
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task OpenForDispositionIfRequiredAsync(
            NonconformanceReport ncr,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }
    }

    private sealed class ObservedRedisCommandLockStore(IRedisCommandLockStore inner) : IRedisCommandLockStore
    {
        private readonly object syncRoot = new();
        private readonly Dictionary<string, int> attempts = new(StringComparer.Ordinal);
        private readonly List<string> acquiredKeys = [];
        private readonly TaskCompletionSource<LockAttempt> secondAttempt =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LockAttempt> SecondAttempt => secondAttempt.Task;

        public IReadOnlyCollection<string> AcquiredKeys
        {
            get
            {
                lock (syncRoot)
                {
                    return acquiredKeys.ToArray();
                }
            }
        }

        public async Task<bool> TryAcquireAsync(
            string key,
            string token,
            TimeSpan leaseTime,
            CancellationToken cancellationToken)
        {
            var acquired = await inner.TryAcquireAsync(key, token, leaseTime, cancellationToken);
            lock (syncRoot)
            {
                attempts.TryGetValue(key, out var attemptCount);
                attemptCount++;
                attempts[key] = attemptCount;
                if (acquired)
                {
                    acquiredKeys.Add(key);
                }

                if (attemptCount == 2)
                {
                    secondAttempt.TrySetResult(new LockAttempt(key, acquired));
                }
            }

            return acquired;
        }

        public Task<bool> RenewAsync(
            string key,
            string token,
            TimeSpan leaseTime,
            CancellationToken cancellationToken) =>
            inner.RenewAsync(key, token, leaseTime, cancellationToken);

        public Task ReleaseAsync(string key, string token, CancellationToken cancellationToken) =>
            inner.ReleaseAsync(key, token, cancellationToken);
    }

    private sealed record LockAttempt(string Key, bool Acquired);

    private sealed class FixedIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext() =>
            new("correlation-ncr-rework-pg", "causation-ncr-rework-pg", "user:qa-manager-001");
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        private int reworkRequestedCalls;

        public int ReworkRequestedCalls => Volatile.Read(ref reworkRequestedCalls);

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (integrationEvent is NcrReworkRequestedIntegrationEvent)
            {
                Interlocked.Increment(ref reworkRequestedCalls);
            }

            return Task.CompletedTask;
        }
    }
}
