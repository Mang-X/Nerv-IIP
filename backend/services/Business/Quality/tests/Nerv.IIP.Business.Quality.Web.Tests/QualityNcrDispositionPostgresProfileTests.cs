using FluentValidation;
using Nerv.IIP.Sdk.FileStorage;
using Nerv.IIP.Business.Quality.Web.Application.Queries.NonconformanceReports;
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
        var files = new QualityEvidenceTestFiles();
        await using var firstProvider = CreateProvider(lockStore, approval, automation, integrationEvents, files.CreateClient());
        await using var secondProvider = CreateProvider(lockStore, approval, automation, integrationEvents, files.CreateClient());
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
            files.Files.Add("evidence-file-001", QualityEvidenceTestFiles.Metadata(first, "evidence-file-001"));
            files.Files.Add("evidence-file-tenant-b", QualityEvidenceTestFiles.Metadata(second, "evidence-file-tenant-b"));
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

        var secondCommand = ReworkCommand(secondNcrId, "org-b", "env-b", reviewedAtUtc)
            with { AttachmentFileIds = ["evidence-file-tenant-b"] };
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

        // 同一受治理 lane 的真实迁移/mediator/UoW 接缝：新增证据合同不新增 lane 或测试注册机制。
        await AssertSortAndScreenEvidencePersistenceAsync(firstProvider, files);
        Assert.Equal(2, approval.NcrCalls);
    }

    private static ServiceProvider CreateProvider(
        ObservedRedisCommandLockStore lockStore,
        ControlledApprovalClient approval,
        RecordingCapaAutomationService automation,
        RecordingIntegrationEventPublisher integrationEvents,
        IFileStorageClient files)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(files);
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

    private static async Task AssertSortAndScreenEvidencePersistenceAsync(
        IServiceProvider provider, QualityEvidenceTestFiles files)
    {
        NonconformanceReportId acceptedId;
        NonconformanceReportId rejectedId;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            var accepted = NewNcr("org-a", "env-a", "NCR-SORT-PG", ["sort-existing"]);
            var rejected = NewNcr("org-a", "env-a", "NCR-INVALID-EVIDENCE-PG", ["invalid-existing"]);
            db.NonconformanceReports.AddRange(accepted, rejected);
            await db.SaveChangesAsync();
            acceptedId = accepted.Id;
            rejectedId = rejected.Id;
            foreach (var id in new[] { "sort-existing", "sort-new" })
                files.Files.Add(id, QualityEvidenceTestFiles.Metadata(accepted, id));
            files.Files.Add("invalid-existing", QualityEvidenceTestFiles.Metadata(rejected, "invalid-existing")
                with { FilePurpose = "engineering-document" });
        }

        await SendInNewScopeAsync(provider, new(acceptedId, "org-a", "env-a", "sort-and-screen", null, ["sort-new"], []));
        await Assert.ThrowsAsync<KnownException>(() => SendInNewScopeAsync(provider,
            new(rejectedId, "org-a", "env-a", "sort-and-screen", null, ["sort-new"], [])));
        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var accepted = await sender.Send(new GetNonconformanceReportQuery(acceptedId, "org-a", "env-a"));
            Assert.Equal("disposition-in-progress", accepted.Status);
            Assert.Equal("sort-and-screen", accepted.DispositionType);
            Assert.Null(accepted.DispositionApprovalChainId);
            Assert.Equal(new[] { "sort-existing", "sort-new" }, accepted.AttachmentFileIds);
            var rejected = await sender.Send(new GetNonconformanceReportQuery(rejectedId, "org-a", "env-a"));
            Assert.Equal("open", rejected.Status);
            Assert.Null(rejected.DispositionType);
            Assert.Equal(new[] { "invalid-existing" }, rejected.AttachmentFileIds);
        }
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

    private static NonconformanceReport NewNcr(string organizationId, string environmentId, string ncrCode,
        IReadOnlyCollection<string>? attachments = null) =>
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
            attachments ?? []);

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
