using System.Data.Common;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

[CollectionDefinition(Name)]
public sealed class MasterDataPostgresProfileCollection
{
    public const string Name = "masterdata-postgres-profile";

    private MasterDataPostgresProfileCollection()
    {
    }
}

[Collection(MasterDataPostgresProfileCollection.Name)]
public sealed class MasterDataPostgresProfileTestsToolingAudit
{
    private const string ToolingAuditPreviousMigration = "20260728232043_AddPrincipalScopeContextAudit";

    [PostgresFact]
    public async Task Tooling_audit_migration_preserves_predecessor_data_and_installs_append_only_schema_on_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var provider = CreateToolingServices(connectionString);

        using (var emptyDatabaseScope = provider.CreateScope())
        {
            var db = emptyDatabaseScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            AssertUsesGovernedDatabase(db);
            await DropMasterDataSchemaAsync(db);
            await db.Database.MigrateAsync();
            await AssertToolingAuditSchemaAsync(db);
            Assert.Empty(await db.ToolingAuditEntries.AsNoTracking().ToArrayAsync());
        }

        await SendAsync(provider, new RegisterToolingAssetCommand(
            "org-append-only",
            "env-append-only",
            "TOOL-APPEND-ONLY",
            "Append-only fixture",
            "fixture",
            ["WC-APPEND-ONLY"],
            ["SKU-APPEND-ONLY"],
            null,
            "op-append-only",
            CreateAuditContext(
                "user:append-only",
                "corr-append-only",
                "cause-append-only",
                "op-append-only")));
        await AssertToolingAuditIsAppendOnlyAsync(provider, "op-append-only");

        ToolingAssetId legacyAssetId;
        using (var predecessorScope = provider.CreateScope())
        {
            var db = predecessorScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DropMasterDataSchemaAsync(db);
            await db.GetService<IMigrator>().MigrateAsync(ToolingAuditPreviousMigration);
            Assert.False(await RelationExistsAsync(db, "tooling_audit_entries"));

            var legacy = ToolingAsset.Register(
                "org-upgrade",
                "env-upgrade",
                "TOOL-UPGRADE",
                "Upgrade fixture",
                "mould",
                ["WC-UPGRADE"],
                ["SKU-UPGRADE"],
                50);
            legacy.RecordUsage(7);
            db.ToolingAssets.Add(legacy);
            await db.SaveChangesAsync();
            legacyAssetId = legacy.Id;
        }

        using (var migrationScope = provider.CreateScope())
        {
            var db = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            await AssertToolingAuditSchemaAsync(db);
        }

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await observer.ToolingAssets
            .AsNoTracking()
            .Include(asset => asset.Applicability)
            .SingleAsync(asset => asset.Code == "TOOL-UPGRADE");
        Assert.Equal(legacyAssetId, persisted.Id);
        Assert.Equal(7, persisted.UsageCount);
        Assert.Equal(ToolingAssetStatus.Available, persisted.Status);
        var applicability = Assert.Single(persisted.Applicability);
        Assert.Equal("WC-UPGRADE", applicability.WorkCenterCode);
        Assert.Equal("SKU-UPGRADE", applicability.SkuCode);
        Assert.Empty(await observer.ToolingAuditEntries.AsNoTracking().ToArrayAsync());
    }

    [PostgresFact]
    public async Task Tooling_commands_commit_business_and_exact_audit_facts_through_mediator_on_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var provider = CreateToolingServices(connectionString);
        await ResetToolingSchemaAsync(provider);

        var registerContext = CreateAuditContext(
            "user:planner-2181",
            "corr-register-2181",
            "cause-register-2181",
            "op-register-2181");
        var registerBefore = DateTimeOffset.UtcNow;
        await SendAsync(provider, new RegisterToolingAssetCommand(
            "org-2181",
            "env-2181",
            "TOOL-2181",
            "Precision fixture",
            "fixture",
            ["WC-2181"],
            ["SKU-2181"],
            100,
            "op-register-2181",
            registerContext));
        var registerAfter = DateTimeOffset.UtcNow;

        ToolingAssetId toolingId;
        using (var registerObserverScope = provider.CreateScope())
        {
            var observer = registerObserverScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tooling = await observer.ToolingAssets.AsNoTracking().SingleAsync();
            toolingId = tooling.Id;
            Assert.Equal(ToolingAssetStatus.Available, tooling.Status);
            Assert.Equal(0, tooling.UsageCount);
            var audit = await observer.ToolingAuditEntries.AsNoTracking().SingleAsync();
            AssertRegisterAudit(
                audit,
                toolingId,
                "org-2181",
                "env-2181",
                "TOOL-2181",
                "user:planner-2181",
                "corr-register-2181",
                "cause-register-2181",
                "op-register-2181",
                registerBefore,
                registerAfter);
        }

        var statusContext = CreateAuditContext(
            "user:maintainer-2181",
            "corr-status-2181",
            "cause-status-2181",
            "op-status-2181");
        var statusBefore = DateTimeOffset.UtcNow;
        await SendAsync(provider, new ChangeToolingStatusCommand(
            "org-2181",
            "env-2181",
            "TOOL-2181",
            ToolingAssetStatus.Maintenance,
            CreateAuditText("planned calibration"),
            statusContext));
        var statusAfter = DateTimeOffset.UtcNow;

        using (var statusObserverScope = provider.CreateScope())
        {
            var observer = statusObserverScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(
                ToolingAssetStatus.Maintenance,
                (await observer.ToolingAssets.AsNoTracking().SingleAsync()).Status);
            var audit = await observer.ToolingAuditEntries.AsNoTracking()
                .SingleAsync(entry => entry.OperationId == "op-status-2181");
            AssertCommonAudit(
                audit,
                toolingId,
                ToolingAuditEntry.StatusOperation,
                "org-2181",
                "env-2181",
                "TOOL-2181",
                "user:maintainer-2181",
                "corr-status-2181",
                "cause-status-2181",
                "op-status-2181",
                statusBefore,
                statusAfter);
            Assert.Equal(ToolingAssetStatus.Available, audit.BeforeStatus);
            Assert.Equal(ToolingAssetStatus.Maintenance, audit.AfterStatus);
            Assert.Equal("planned calibration", audit.Reason);
            Assert.Null(audit.BeforeUsageCount);
            Assert.Null(audit.AfterUsageCount);
            Assert.Null(audit.UsageDelta);
        }

        var usageContext = CreateAuditContext(
            "user:operator-2181",
            "corr-usage-2181",
            "cause-usage-2181",
            "op-usage-2181");
        var usageBefore = DateTimeOffset.UtcNow;
        await SendAsync(provider, new RecordToolingUsageCommand(
            "org-2181",
            "env-2181",
            "TOOL-2181",
            9,
            usageContext));
        var usageAfter = DateTimeOffset.UtcNow;

        using var usageObserverScope = provider.CreateScope();
        var usageObserver = usageObserverScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(9, (await usageObserver.ToolingAssets.AsNoTracking().SingleAsync()).UsageCount);
        var usageAudit = await usageObserver.ToolingAuditEntries.AsNoTracking()
            .SingleAsync(entry => entry.OperationId == "op-usage-2181");
        AssertCommonAudit(
            usageAudit,
            toolingId,
            ToolingAuditEntry.UsageOperation,
            "org-2181",
            "env-2181",
            "TOOL-2181",
            "user:operator-2181",
            "corr-usage-2181",
            "cause-usage-2181",
            "op-usage-2181",
            usageBefore,
            usageAfter);
        Assert.Equal(0, usageAudit.BeforeUsageCount);
        Assert.Equal(9, usageAudit.AfterUsageCount);
        Assert.Equal(9, usageAudit.UsageDelta);
        Assert.Null(usageAudit.BeforeStatus);
        Assert.Null(usageAudit.AfterStatus);
        Assert.Null(usageAudit.Reason);
        Assert.Equal(3, await usageObserver.ToolingAuditEntries.CountAsync());
    }

    [PostgresFact]
    public async Task Tooling_replays_are_idempotent_and_conflicting_payloads_preserve_first_winner_on_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var provider = CreateToolingServices(connectionString);
        await ResetToolingSchemaAsync(provider);

        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ToolingAssets.Add(ToolingAsset.Register(
                "org-replay", "env-replay", "TOOL-B", "Tool B", "fixture", ["WC-B"], ["SKU-B"], null));
            await db.SaveChangesAsync();
        }

        var register = new RegisterToolingAssetCommand(
            "org-replay",
            "env-replay",
            "TOOL-A",
            "Tool A",
            "mould",
            ["WC-A"],
            ["SKU-A"],
            100,
            "op-register-replay",
            CreateAuditContext("user:planner", "corr-register", "cause-register", "op-register-replay"));
        await SendAsync(provider, register);
        await SendAsync(provider, register);
        await Assert.ThrowsAsync<KnownException>(() => SendAsync(provider, register with { Code = "TOOL-UNUSED" }));

        var usage = new RecordToolingUsageCommand(
            "org-replay",
            "env-replay",
            "TOOL-A",
            7,
            CreateAuditContext("user:operator", "corr-usage", "cause-usage", "op-usage-replay"));
        await SendAsync(provider, usage);
        await SendAsync(provider, usage);
        await Assert.ThrowsAsync<KnownException>(() => SendAsync(provider, usage with { Count = 8 }));
        await Assert.ThrowsAsync<KnownException>(() => SendAsync(provider, usage with { Code = "TOOL-B" }));

        var status = new ChangeToolingStatusCommand(
            "org-replay",
            "env-replay",
            "TOOL-A",
            ToolingAssetStatus.Maintenance,
            CreateAuditText("planned service"),
            CreateAuditContext("user:maintainer", "corr-status", "cause-status", "op-status-replay"));
        await SendAsync(provider, status);
        await SendAsync(provider, status);
        await Assert.ThrowsAsync<KnownException>(() => SendAsync(
            provider,
            status with { Status = ToolingAssetStatus.Available }));
        await Assert.ThrowsAsync<KnownException>(() => SendAsync(
            provider,
            status with { Reason = CreateAuditText("different reason") }));
        await Assert.ThrowsAsync<KnownException>(() => SendAsync(provider, status with { Code = "TOOL-B" }));

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstWinner = await observer.ToolingAssets.AsNoTracking().SingleAsync(asset => asset.Code == "TOOL-A");
        var adjacent = await observer.ToolingAssets.AsNoTracking().SingleAsync(asset => asset.Code == "TOOL-B");
        Assert.Equal(7, firstWinner.UsageCount);
        Assert.Equal(ToolingAssetStatus.Maintenance, firstWinner.Status);
        Assert.Equal(0, adjacent.UsageCount);
        Assert.Equal(ToolingAssetStatus.Available, adjacent.Status);
        Assert.False(await observer.ToolingAssets.AnyAsync(asset => asset.Code == "TOOL-UNUSED"));
        Assert.Equal(3, await observer.ToolingAuditEntries.CountAsync());
        Assert.Equal("planned service", (await observer.ToolingAuditEntries
            .SingleAsync(entry => entry.OperationId == "op-status-replay")).Reason);
    }

    [PostgresFact]
    public async Task Tooling_concurrent_usage_replay_commits_one_increment_and_one_audit_on_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var concurrencyProbe = new ToolingConcurrencyProbe();
        await using var provider = CreateToolingServices(connectionString, concurrencyProbe: concurrencyProbe);
        await ResetToolingSchemaAsync(provider);

        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ToolingAssets.Add(ToolingAsset.Register(
                "org-race", "env-race", "TOOL-RACE", "Race fixture", "fixture", ["WC-RACE"], ["SKU-RACE"], 100));
            await db.SaveChangesAsync();
        }

        var command = new RecordToolingUsageCommand(
            "org-race",
            "env-race",
            "TOOL-RACE",
            5,
            CreateAuditContext("user:operator-race", "corr-race", "cause-race", "op-usage-race"));
        using var firstScope = provider.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstUnitOfWork = (ITransactionUnitOfWork)firstDb;
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
        firstUnitOfWork.CurrentTransaction = firstTransaction;
        await firstScope.ServiceProvider.GetRequiredService<IMediator>().Send(command);
        Assert.True(await HasGrantedAdvisoryLockAsync(connectionString, ((NpgsqlConnection)firstDb.Database.GetDbConnection()).ProcessID));

        using var secondScope = provider.CreateScope();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondUnitOfWork = (ITransactionUnitOfWork)secondDb;
        await using var secondTransaction = await secondDb.Database.BeginTransactionAsync();
        secondUnitOfWork.CurrentTransaction = secondTransaction;
        concurrencyProbe.Arm(((NpgsqlConnection)secondDb.Database.GetDbConnection()).ProcessID);
        var secondSend = secondScope.ServiceProvider.GetRequiredService<IMediator>().Send(command);

        var observedBoundary = await concurrencyProbe.ObserveBoundaryAsync();
        var completedBeforeFirstCommit = secondSend.IsCompleted;
        var waitedOnAdvisoryLock = await HasWaitingAdvisoryLockAsync(
            connectionString,
            ((NpgsqlConnection)secondDb.Database.GetDbConnection()).ProcessID);

        await firstTransaction.CommitAsync();
        firstUnitOfWork.CurrentTransaction = null;
        await secondSend;
        await secondTransaction.CommitAsync();
        secondUnitOfWork.CurrentTransaction = null;
        Assert.Equal(ToolingConcurrencyBoundary.AdvisoryLockAttempt, observedBoundary);
        Assert.False(completedBeforeFirstCommit);
        Assert.True(waitedOnAdvisoryLock);

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(5, (await observer.ToolingAssets.AsNoTracking().SingleAsync()).UsageCount);
        var audit = await observer.ToolingAuditEntries.AsNoTracking().SingleAsync();
        Assert.Equal("op-usage-race", audit.OperationId);
        Assert.Equal(0, audit.BeforeUsageCount);
        Assert.Equal(5, audit.AfterUsageCount);
        Assert.Equal(5, audit.UsageDelta);
    }

    [PostgresFact]
    public async Task Tooling_save_failure_rolls_back_business_and_audit_together_on_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var interceptor = new CorruptPendingToolingAuditInterceptor("op-usage-rollback");
        await using var provider = CreateToolingServices(connectionString, interceptor);
        await ResetToolingSchemaAsync(provider);

        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ToolingAssets.Add(ToolingAsset.Register(
                "org-rollback", "env-rollback", "TOOL-ROLLBACK", "Rollback fixture", "fixture", ["WC-RB"], ["SKU-RB"], null));
            await db.SaveChangesAsync();
        }

        var command = new RecordToolingUsageCommand(
            "org-rollback",
            "env-rollback",
            "TOOL-ROLLBACK",
            11,
            CreateAuditContext("user:operator-rb", "corr-rb", "cause-rb", "op-usage-rollback"));
        await Assert.ThrowsAsync<DbUpdateException>(() => SendAsync(provider, command));
        Assert.True(interceptor.ObservedBusinessAndAuditPending);

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, (await observer.ToolingAssets.AsNoTracking().SingleAsync()).UsageCount);
        Assert.Empty(await observer.ToolingAuditEntries.AsNoTracking().ToArrayAsync());
    }

    [PostgresFact]
    public async Task Tooling_audit_is_scoped_and_excludes_sensitive_request_content_on_postgres()
    {
        const string sensitiveSentinel = "sensitive-request-sentinel-2181";
        const string authorizationSentinel = "authorization-sentinel-2181";
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var provider = CreateToolingServices(connectionString);
        await ResetToolingSchemaAsync(provider);

        var first = new RegisterToolingAssetCommand(
            "org-scope-a",
            "env-scope-a",
            "TOOL-SCOPED",
            sensitiveSentinel,
            "fixture",
            ["WC-A"],
            ["SKU-A"],
            null,
            "op-shared-scope",
            CreateAuditContext(
                "user:scope-a",
                "corr-scope-a",
                "cause-scope-a",
                "op-shared-scope",
                authorizationSentinel));
        var second = new RegisterToolingAssetCommand(
            "org-scope-b",
            "env-scope-b",
            "TOOL-SCOPED",
            "Adjacent tenant fixture",
            "fixture",
            ["WC-B"],
            ["SKU-B"],
            null,
            "op-shared-scope",
            CreateAuditContext(
                "user:scope-b",
                "corr-scope-b",
                "cause-scope-b",
                "op-shared-scope",
                authorizationSentinel));
        await SendAsync(provider, first);
        await SendAsync(provider, second);

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstScopeAudits = await observer.ToolingAuditEntries.AsNoTracking()
            .Where(entry => entry.OrganizationId == "org-scope-a" && entry.EnvironmentId == "env-scope-a")
            .ToArrayAsync();
        var firstAudit = Assert.Single(firstScopeAudits);
        Assert.Equal("user:scope-a", firstAudit.ActorId);
        Assert.DoesNotContain(sensitiveSentinel, AuditText(firstAudit), StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationSentinel, AuditText(firstAudit), StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", AuditText(firstAudit), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", AuditText(firstAudit), StringComparison.OrdinalIgnoreCase);

        var secondScopeAudits = await observer.ToolingAuditEntries.AsNoTracking()
            .Where(entry => entry.OrganizationId == "org-scope-b" && entry.EnvironmentId == "env-scope-b")
            .ToArrayAsync();
        var secondAudit = Assert.Single(secondScopeAudits);
        Assert.Equal("user:scope-b", secondAudit.ActorId);
        Assert.Equal(2, await observer.ToolingAuditEntries.CountAsync());
        Assert.Equal(2, await observer.ToolingAssets.CountAsync());
    }

    private static ServiceProvider CreateToolingServices(
        string connectionString,
        SaveChangesInterceptor? interceptor = null,
        ToolingConcurrencyProbe? concurrencyProbe = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
            configuration.AddUnitOfWorkBehaviors();
        });
        services.AddMasterDataPostgreSqlPersistence(connectionString);
        services.AddScoped<MasterDataCodingService>();
        if (interceptor is not null)
        {
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(interceptor));
        }
        if (concurrencyProbe is not null)
        {
            services.AddDbContext<ApplicationDbContext>(options =>
                options.AddInterceptors(concurrencyProbe.CommandInterceptor));
            services.AddScoped<IToolingAuditOperationCoordinator>(serviceProvider =>
                new ObservedToolingAuditOperationCoordinator(
                    new PostgreSqlToolingAuditOperationCoordinator(
                        serviceProvider.GetRequiredService<ApplicationDbContext>()),
                    concurrencyProbe));
        }

        return services.BuildServiceProvider();
    }

    private static async Task ResetToolingSchemaAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        AssertUsesGovernedDatabase(db);
        await DropMasterDataSchemaAsync(db);
        await db.Database.MigrateAsync();
    }

    private static async Task<MasterDataResourceResult> SendAsync(
        ServiceProvider provider,
        RegisterToolingAssetCommand command)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command);
    }

    private static async Task SendAsync(ServiceProvider provider, ChangeToolingStatusCommand command)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command);
    }

    private static async Task SendAsync(ServiceProvider provider, RecordToolingUsageCommand command)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command);
    }

    private static ToolingOperationAuditContext CreateAuditContext(
        string actor,
        string correlationId,
        string causationId,
        string operationId,
        string authorizationCredential = "tooling-audit-test-credential") =>
        CreateAdmission(actor, correlationId, causationId, operationId, authorizationCredential)
            .GetRequiredContext();

    private static ToolingOperationAuditContext.ToolingAuditSafeText CreateAuditText(string value) =>
        CreateAdmission(
                "user:audit-text",
                "corr-audit-text",
                "cause-audit-text",
                "op-audit-text",
                "tooling-audit-test-credential")
            .RequireAuditSafeText(value, "reason");

    private static IToolingOperationAdmission CreateAdmission(
        string actor,
        string correlationId,
        string causationId,
        string operationId,
        string authorizationCredential)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("token_type", "internal_service")],
                "postgres-test"))
        };
        httpContext.Request.Headers.Authorization = $"Bearer {authorizationCredential}";
        httpContext.Request.Headers["X-Authenticated-Actor"] = actor;
        httpContext.Request.Headers["X-Correlation-Id"] = correlationId;
        httpContext.Request.Headers["X-Causation-Id"] = causationId;
        httpContext.Request.Headers["X-Idempotency-Key"] = operationId;
        return new ToolingOperationAuditContext.ToolingAuditSafeText.HttpAdmission(new HttpContextAccessor
        {
            HttpContext = httpContext
        });
    }

    private static void AssertRegisterAudit(
        ToolingAuditEntry audit,
        ToolingAssetId toolingId,
        string organizationId,
        string environmentId,
        string toolingCode,
        string actor,
        string correlationId,
        string causationId,
        string operationId,
        DateTimeOffset earliest,
        DateTimeOffset latest)
    {
        AssertCommonAudit(
            audit,
            toolingId,
            ToolingAuditEntry.RegisterOperation,
            organizationId,
            environmentId,
            toolingCode,
            actor,
            correlationId,
            causationId,
            operationId,
            earliest,
            latest);
        Assert.Null(audit.BeforeStatus);
        Assert.Equal(ToolingAssetStatus.Available, audit.AfterStatus);
        Assert.Null(audit.BeforeUsageCount);
        Assert.Equal(0, audit.AfterUsageCount);
        Assert.Null(audit.UsageDelta);
        Assert.Null(audit.Reason);
    }

    private static void AssertCommonAudit(
        ToolingAuditEntry audit,
        ToolingAssetId toolingId,
        string operationKind,
        string organizationId,
        string environmentId,
        string toolingCode,
        string actor,
        string correlationId,
        string causationId,
        string operationId,
        DateTimeOffset earliest,
        DateTimeOffset latest)
    {
        Assert.Equal(operationKind, audit.OperationKind);
        Assert.Equal(organizationId, audit.OrganizationId);
        Assert.Equal(environmentId, audit.EnvironmentId);
        Assert.Equal(toolingId.ToString(), audit.ToolingAssetId);
        Assert.Equal(toolingCode, audit.ToolingCode);
        Assert.Equal(actor, audit.ActorId);
        Assert.Equal(correlationId, audit.CorrelationId);
        Assert.Equal(causationId, audit.CausationId);
        Assert.Equal(operationId, audit.OperationId);
        Assert.Equal(64, audit.RequestFingerprint.Length);
        Assert.Equal(TimeSpan.Zero, audit.OccurredAtUtc.Offset);
        Assert.InRange(audit.OccurredAtUtc, earliest, latest);
    }

    private static string AuditText(ToolingAuditEntry audit) => string.Join('|',
        audit.OrganizationId,
        audit.EnvironmentId,
        audit.OperationKind,
        audit.ToolingAssetId,
        audit.ToolingCode,
        audit.ActorId,
        audit.CorrelationId,
        audit.CausationId,
        audit.OperationId,
        audit.RequestFingerprint,
        audit.BeforeStatus,
        audit.AfterStatus,
        audit.BeforeUsageCount,
        audit.AfterUsageCount,
        audit.UsageDelta,
        audit.Reason,
        audit.OccurredAtUtc);

    private static async Task AssertToolingAuditSchemaAsync(ApplicationDbContext db)
    {
        Assert.True(await RelationExistsAsync(db, "tooling_audit_entries"));
        Assert.Equal(
            "Append-only audit facts for governed tooling register, status, and usage operations.",
            await ExecuteScalarAsync<string>(db,
                "SELECT obj_description('business_masterdata.tooling_audit_entries'::regclass, 'pg_class')"));
        Assert.Equal(0L, await ExecuteScalarAsync<long>(db,
            """
            SELECT COUNT(*)
            FROM pg_attribute AS attribute
            WHERE attribute.attrelid = 'business_masterdata.tooling_audit_entries'::regclass
              AND attribute.attnum > 0
              AND NOT attribute.attisdropped
              AND col_description(attribute.attrelid, attribute.attnum) IS NULL
            """));
        Assert.Equal(1L, await ExecuteScalarAsync<long>(db,
            """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = 'business_masterdata'
              AND tablename = 'tooling_audit_entries'
              AND indexname = 'ux_tooling_audit_operation'
              AND indexdef LIKE 'CREATE UNIQUE INDEX%\"OrganizationId\", \"EnvironmentId\", \"OperationId\"%'
            """));
        Assert.Equal(1L, await ExecuteScalarAsync<long>(db,
            """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = 'business_masterdata'
              AND tablename = 'tooling_audit_entries'
              AND indexname = 'ix_tooling_audit_target_time'
              AND indexdef LIKE '%\"OrganizationId\", \"EnvironmentId\", \"ToolingCode\", \"OccurredAtUtc\"%'
            """));
        Assert.Equal(1L, await ExecuteScalarAsync<long>(db,
            """
            SELECT COUNT(*)
            FROM pg_trigger
            WHERE tgrelid = 'business_masterdata.tooling_audit_entries'::regclass
              AND tgname = 'trg_tooling_audit_append_only'
              AND NOT tgisinternal
            """));
    }

    private static async Task<bool> RelationExistsAsync(ApplicationDbContext db, string relation)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT to_regclass(@relation) IS NOT NULL";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "relation";
        parameter.Value = $"business_masterdata.{relation}";
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<T> ExecuteScalarAsync<T>(ApplicationDbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertToolingAuditIsAppendOnlyAsync(
        ServiceProvider provider,
        string operationId)
    {
        using (var updateScope = provider.CreateScope())
        {
            var updateDb = updateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var exception = await Assert.ThrowsAsync<PostgresException>(() => updateDb.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE business_masterdata.tooling_audit_entries SET \"ActorId\" = {"tampered"} WHERE \"OperationId\" = {operationId}"));
            Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        }

        using (var deleteScope = provider.CreateScope())
        {
            var deleteDb = deleteScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var exception = await Assert.ThrowsAsync<PostgresException>(() => deleteDb.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM business_masterdata.tooling_audit_entries WHERE \"OperationId\" = {operationId}"));
            Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        }

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await observer.ToolingAuditEntries.AsNoTracking().SingleAsync(entry => entry.OperationId == operationId);
        Assert.Equal("user:append-only", audit.ActorId);
    }

    private static async Task<bool> HasGrantedAdvisoryLockAsync(string connectionString, int processId) =>
        await CountAdvisoryLocksAsync(connectionString, processId, granted: true) > 0;

    private static async Task<bool> HasWaitingAdvisoryLockAsync(string connectionString, int processId) =>
        await CountAdvisoryLocksAsync(connectionString, processId, granted: false) > 0;

    private static async Task<long> CountAdvisoryLocksAsync(
        string connectionString,
        int processId,
        bool granted)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pg_locks WHERE pid = @pid AND locktype = 'advisory' AND granted = @granted",
            connection);
        command.Parameters.AddWithValue("pid", processId);
        command.Parameters.AddWithValue("granted", granted);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task DropMasterDataSchemaAsync(ApplicationDbContext db)
    {
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(MasterDataFacts.Schema);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private static void AssertUsesGovernedDatabase(ApplicationDbContext db)
    {
        var governedConnection = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        Assert.Equal(governedConnection.Database, db.Database.GetDbConnection().Database);
    }

    private sealed class CorruptPendingToolingAuditInterceptor(string operationId) : SaveChangesInterceptor
    {
        public bool ObservedBusinessAndAuditPending { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            var audit = context.ChangeTracker.Entries<ToolingAuditEntry>()
                .SingleOrDefault(entry =>
                    entry.State == EntityState.Added &&
                    entry.Entity.OperationId == operationId);
            var business = context.ChangeTracker.Entries<ToolingAsset>()
                .SingleOrDefault(entry => entry.State == EntityState.Modified);
            if (audit is not null && business is not null)
            {
                ObservedBusinessAndAuditPending = true;
                audit.Property(nameof(ToolingAuditEntry.OperationKind)).CurrentValue = "invalid";
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private enum ToolingConcurrencyBoundary
    {
        AdvisoryLockAttempt,
        ActionEntered,
    }

    private sealed class ToolingConcurrencyProbe
    {
        private TaskCompletionSource<ToolingConcurrencyBoundary>? boundary;
        private int processId;

        public DbCommandInterceptor CommandInterceptor { get; }

        public ToolingConcurrencyProbe()
        {
            CommandInterceptor = new ToolingConcurrencyCommandInterceptor(this);
        }

        public void Arm(int armedProcessId)
        {
            processId = armedProcessId;
            boundary = new TaskCompletionSource<ToolingConcurrencyBoundary>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task<ToolingConcurrencyBoundary> ObserveBoundaryAsync() => boundary!.Task;

        public void ObserveAdvisoryLockAttempt(DbCommand command)
        {
            if (boundary is not null &&
                command.Connection is NpgsqlConnection connection &&
                connection.ProcessID == processId &&
                command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
            {
                boundary.TrySetResult(ToolingConcurrencyBoundary.AdvisoryLockAttempt);
            }
        }

        public void ObserveActionEntered() =>
            boundary?.TrySetResult(ToolingConcurrencyBoundary.ActionEntered);
    }

    private sealed class ToolingConcurrencyCommandInterceptor(ToolingConcurrencyProbe probe) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            probe.ObserveAdvisoryLockAttempt(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ObservedToolingAuditOperationCoordinator(
        IToolingAuditOperationCoordinator inner,
        ToolingConcurrencyProbe probe) : IToolingAuditOperationCoordinator
    {
        public Task<T> ExecuteAsync<T>(
            string organizationId,
            string environmentId,
            string operationId,
            string? toolingCode,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            inner.ExecuteAsync(
                organizationId,
                environmentId,
                operationId,
                toolingCode,
                token =>
                {
                    probe.ObserveActionEntered();
                    return action(token);
                },
                cancellationToken);
    }
}
