using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class ToolingAuditTests
{
    [Fact]
    public async Task Register_replay_persists_one_whitelisted_audit_fact()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new RegisterToolingAssetCommandHandler(
            new ToolingAssetRepository(dbContext),
            new MasterDataCodingService(),
            dbContext);
        var context = TrustedContext("corr-register", "cause-register", "user:planner-001", "tooling-register-001");
        var command = new RegisterToolingAssetCommand(
            "org-001", "env-dev", " TOOL-001 ", "敏感工装名称", "mould",
            [" WC-01 "], [" SKU-A "], 100, "tooling-register-001", context);

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var replay = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            command with { Name = "另一个工装名称" },
            CancellationToken.None));

        Assert.Equal(first, replay);
        var audit = await dbContext.ToolingAuditEntries.SingleAsync();
        var tooling = await dbContext.ToolingAssets.SingleAsync();
        Assert.Equal("tooling-register", audit.OperationKind);
        Assert.Equal(tooling.Id.ToString(), audit.ToolingAssetId);
        Assert.Equal("TOOL-001", audit.ToolingCode);
        Assert.Equal("user:planner-001", audit.ActorId);
        Assert.Equal("corr-register", audit.CorrelationId);
        Assert.Equal("cause-register", audit.CausationId);
        Assert.Equal("tooling-register-001", audit.OperationId);
        Assert.Null(audit.BeforeStatus);
        Assert.Equal(ToolingAssetStatus.Available, audit.AfterStatus);
        Assert.DoesNotContain("敏感工装名称", AuditText(audit), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_and_usage_record_before_after_summaries_and_reject_conflicting_replay()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = new ToolingAssetRepository(dbContext);
        var tooling = ToolingAsset.Register(
            "org-001", "env-dev", "TOOL-002", "Tool 2", "mould", ["WC-01"], ["SKU-A"], 100);
        dbContext.ToolingAssets.Add(tooling);
        await dbContext.SaveChangesAsync();

        var usageHandler = new RecordToolingUsageCommandHandler(repository, dbContext);
        var usage = new RecordToolingUsageCommand(
            "org-001", "env-dev", "TOOL-002", 12,
            TrustedContext("corr-usage", "cause-usage", "user:operator-001", "tooling-usage-001"));
        await usageHandler.Handle(usage, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await usageHandler.Handle(usage, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(12, tooling.UsageCount);
        var usageAudit = await dbContext.ToolingAuditEntries.SingleAsync(x => x.OperationKind == "tooling-usage");
        Assert.Equal(0, usageAudit.BeforeUsageCount);
        Assert.Equal(12, usageAudit.AfterUsageCount);
        Assert.Equal(12, usageAudit.UsageDelta);
        await Assert.ThrowsAsync<KnownException>(() => usageHandler.Handle(
            usage with { Count = 13 }, CancellationToken.None));
        Assert.Equal(12, tooling.UsageCount);

        var statusHandler = new ChangeToolingStatusCommandHandler(repository, dbContext);
        var status = new ChangeToolingStatusCommand(
            "org-001", "env-dev", "TOOL-002", ToolingAssetStatus.Maintenance, " planned service ",
            TrustedContext("corr-status", "cause-status", "user:planner-002", "tooling-status-001"));
        await statusHandler.Handle(status, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await statusHandler.Handle(status, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var statusAudit = await dbContext.ToolingAuditEntries.SingleAsync(x => x.OperationKind == "tooling-status");
        Assert.Equal(ToolingAssetStatus.Available, statusAudit.BeforeStatus);
        Assert.Equal(ToolingAssetStatus.Maintenance, statusAudit.AfterStatus);
        Assert.Equal("planned service", statusAudit.Reason);
    }

    [Fact]
    public async Task Same_operation_identity_rejects_different_operation_target_or_summary_before_mutation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = new ToolingAssetRepository(dbContext);
        var first = ToolingAsset.Register(
            "org-001", "env-dev", "TOOL-101", "Tool 101", "mould", ["WC-01"], ["SKU-A"], null);
        var second = ToolingAsset.Register(
            "org-001", "env-dev", "TOOL-102", "Tool 102", "mould", ["WC-01"], ["SKU-A"], null);
        dbContext.ToolingAssets.AddRange(first, second);
        await dbContext.SaveChangesAsync();

        var context = TrustedContext("corr-conflict", "cause-conflict", "user:operator", "tooling-operation-shared");
        var usageHandler = new RecordToolingUsageCommandHandler(repository, dbContext);
        await usageHandler.Handle(
            new RecordToolingUsageCommand("org-001", "env-dev", "TOOL-101", 5, context),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<KnownException>(() => usageHandler.Handle(
            new RecordToolingUsageCommand("org-001", "env-dev", "TOOL-101", 6, context),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => usageHandler.Handle(
            new RecordToolingUsageCommand("org-001", "env-dev", "TOOL-102", 5, context),
            CancellationToken.None));
        var statusHandler = new ChangeToolingStatusCommandHandler(repository, dbContext);
        await Assert.ThrowsAsync<KnownException>(() => statusHandler.Handle(
            new ChangeToolingStatusCommand(
                "org-001", "env-dev", "TOOL-101", ToolingAssetStatus.Maintenance, "service", context),
            CancellationToken.None));

        Assert.Equal(5, first.UsageCount);
        Assert.Equal(0, second.UsageCount);
        Assert.Equal(ToolingAssetStatus.Available, first.Status);
        Assert.Single(dbContext.ToolingAuditEntries);
    }

    [Fact]
    public async Task Missing_trusted_actor_or_operation_identity_fails_before_business_mutation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = new ToolingAssetRepository(dbContext);
        var tooling = ToolingAsset.Register(
            "org-001", "env-dev", "TOOL-003", "Tool 3", "mould", ["WC-01"], ["SKU-A"], null);
        dbContext.ToolingAssets.Add(tooling);
        await dbContext.SaveChangesAsync();
        var handler = new RecordToolingUsageCommandHandler(repository, dbContext);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new RecordToolingUsageCommand(
                "org-001", "env-dev", "TOOL-003", 1,
                new MasterDataIntegrationEventContext("corr", "cause", "system:business-masterdata", "operation", false)),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new RecordToolingUsageCommand(
                "org-001", "env-dev", "TOOL-003", 1,
                new MasterDataIntegrationEventContext("corr", "cause", "user:operator", null, true)),
            CancellationToken.None));

        Assert.Equal(0, tooling.UsageCount);
        Assert.Empty(dbContext.ToolingAuditEntries);
    }

    [Fact]
    public async Task Audit_constraint_failure_rolls_back_usage_and_audit_together_on_sqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseSqlite(connection)
            .AddInterceptors(new CorruptToolingAuditInterceptor()));
        await using var provider = services.BuildServiceProvider();
        using (var seedScope = provider.CreateScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await seed.Database.EnsureCreatedAsync();
            seed.ToolingAssets.Add(ToolingAsset.Register(
                "org-001", "env-dev", "TOOL-004", "Tool 4", "mould", ["WC-01"], ["SKU-A"], null));
            await seed.SaveChangesAsync();
        }

        using (var commandScope = provider.CreateScope())
        {
            var dbContext = commandScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var handler = new RecordToolingUsageCommandHandler(new ToolingAssetRepository(dbContext), dbContext);
            await handler.Handle(new RecordToolingUsageCommand(
                "org-001", "env-dev", "TOOL-004", 5,
                TrustedContext("corr-rollback", "cause-rollback", "user:operator", "tooling-usage-rollback")), CancellationToken.None);
            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        }

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, (await observer.ToolingAssets.AsNoTracking().SingleAsync()).UsageCount);
        Assert.Empty(await observer.ToolingAuditEntries.AsNoTracking().ToArrayAsync());
    }

    private static MasterDataIntegrationEventContext TrustedContext(
        string correlationId,
        string causationId,
        string actor,
        string operationId) => new(correlationId, causationId, actor, operationId, true);

    private static string AuditText(ToolingAuditEntry audit) => string.Join('|',
        audit.OperationKind,
        audit.ToolingAssetId,
        audit.ToolingCode,
        audit.ActorId,
        audit.CorrelationId,
        audit.CausationId,
        audit.OperationId,
        audit.RequestFingerprint,
        audit.Reason);

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"tooling-audit-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private sealed class CorruptToolingAuditInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var entry = eventData.Context?.ChangeTracker.Entries<ToolingAuditEntry>()
                .SingleOrDefault(candidate => candidate.State == EntityState.Added);
            if (entry is not null)
            {
                entry.Property(nameof(ToolingAuditEntry.OperationKind)).CurrentValue = "invalid";
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
