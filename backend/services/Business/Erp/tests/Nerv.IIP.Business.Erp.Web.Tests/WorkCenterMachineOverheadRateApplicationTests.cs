using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WorkCenterMachineOverheadRateApplicationTests
{
    private static readonly DateTimeOffset ChangedAtUtc =
        new(2026, 5, 25, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Configure_assigns_append_only_revisions_inside_period_scope()
    {
        await using var db = CreateDb();
        db.AccountingPeriods.AddRange(
            Period("org-a", "env-a", "2026-06", 6),
            Period("org-a", "env-a", "2026-07", 7));
        await db.SaveChangesAsync();
        var handler = new ConfigureWorkCenterMachineOverheadRateCommandHandler(
            db,
            new PostgreSqlErpAdvisoryLockAllocator(db));

        await handler.Handle(ApplicableCommand("2026-06", 30_000m), CancellationToken.None);
        await handler.Handle(ApplicableCommand("2026-06", 31_000m), CancellationToken.None);
        await handler.Handle(ApplicableCommand("2026-07", 32_000m), CancellationToken.None);
        await db.SaveChangesAsync();

        var juneRevisions = await db.WorkCenterMachineOverheadRates
            .Where(x => x.AccountingPeriodCode == "2026-06")
            .OrderBy(x => x.Revision)
            .Select(x => x.Revision)
            .ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, juneRevisions);
        Assert.Equal(
            1,
            await db.WorkCenterMachineOverheadRates
                .Where(x => x.AccountingPeriodCode == "2026-07")
                .Select(x => x.Revision)
                .SingleAsync());
    }

    [Fact]
    public async Task Configure_fails_closed_when_scoped_accounting_period_is_missing()
    {
        await using var db = CreateDb();
        db.AccountingPeriods.Add(Period("org-other", "env-a", "2026-06", 6));
        await db.SaveChangesAsync();
        var handler = new ConfigureWorkCenterMachineOverheadRateCommandHandler(
            db,
            new PostgreSqlErpAdvisoryLockAllocator(db));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(ApplicableCommand("2026-06", 30_000m), CancellationToken.None));

        Assert.Contains("会计期间", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await db.WorkCenterMachineOverheadRates.ToListAsync());
    }

    [Fact]
    public async Task Configure_handler_rejects_unknown_applicability_instead_of_treating_it_as_not_applicable()
    {
        await using var db = CreateDb();
        db.AccountingPeriods.Add(Period("org-a", "env-a", "2026-06", 6));
        await db.SaveChangesAsync();
        var handler = new ConfigureWorkCenterMachineOverheadRateCommandHandler(
            db,
            new PostgreSqlErpAdvisoryLockAllocator(db));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.Handle(
            ApplicableCommand("2026-06", 30_000m) with
            {
                Applicability = (MachineOverheadApplicability)99,
                FixedOverheadBudget = 0m,
                VariableOverheadBudget = 0m,
                NormalCapacityMachineHours = 0m,
            },
            CancellationToken.None));
        Assert.Empty(await db.WorkCenterMachineOverheadRates.ToListAsync());
    }

    [Fact]
    public async Task Configure_handler_does_not_discard_cost_values_from_a_not_applicable_request()
    {
        await using var db = CreateDb();
        db.AccountingPeriods.Add(Period("org-a", "env-a", "2026-06", 6));
        await db.SaveChangesAsync();
        var handler = new ConfigureWorkCenterMachineOverheadRateCommandHandler(
            db,
            new PostgreSqlErpAdvisoryLockAllocator(db));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.Handle(
            ApplicableCommand("2026-06", 30_000m) with
            {
                Applicability = MachineOverheadApplicability.NotApplicable,
            },
            CancellationToken.None));
        Assert.Empty(await db.WorkCenterMachineOverheadRates.ToListAsync());
    }

    [Fact]
    public async Task Configure_rejects_currency_drift_across_periods_in_work_center_scope()
    {
        await using var db = CreateDb();
        db.AccountingPeriods.AddRange(
            Period("org-a", "env-a", "2026-06", 6),
            Period("org-a", "env-a", "2026-07", 7));
        await db.SaveChangesAsync();
        var handler = new ConfigureWorkCenterMachineOverheadRateCommandHandler(
            db,
            new PostgreSqlErpAdvisoryLockAllocator(db));
        await handler.Handle(ApplicableCommand("2026-06", 30_000m), CancellationToken.None);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            ApplicableCommand("2026-07", 30_000m) with { CurrencyCode = "USD" },
            CancellationToken.None));

        Assert.Contains("币种已固定", exception.Message, StringComparison.Ordinal);
        Assert.Single(await db.WorkCenterMachineOverheadRates.ToListAsync());
    }

    [Fact]
    public async Task Revision_lock_key_uses_the_currency_scope_not_an_individual_period()
    {
        await using var db = CreateDb();
        var revisionLock = new PostgreSqlErpAdvisoryLockAllocator(db);

        var key = revisionLock.GetLockKey(
            ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate,
            " org-a ", " env-a ", " WC-01 ");

        Assert.Equal(key, revisionLock.GetLockKey(ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate, "org-a", "env-a", "WC-01"));
        Assert.NotEqual(key, revisionLock.GetLockKey(ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate, "org-b", "env-a", "WC-01"));
        Assert.NotEqual(key, revisionLock.GetLockKey(ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate, "org-a", "env-b", "WC-01"));
        Assert.NotEqual(key, revisionLock.GetLockKey(ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate, "org-a", "env-a", "WC-02"));
        Assert.NotEqual(
            key,
            revisionLock.GetLockKey(ErpAdvisoryLockDomain.WorkCenterLaborCostRate, "org-a", "env-a", "WC-01"));
    }

    [Fact]
    public async Task Audit_query_returns_only_exact_scope_and_period_history()
    {
        await using var db = CreateDb();
        db.WorkCenterMachineOverheadRates.AddRange(
            Rate("org-a", "env-a", "WC-01", "2026-06", 1, 30_000m),
            Rate("org-a", "env-a", "WC-01", "2026-06", 2, 31_000m),
            Rate("org-a", "env-a", "WC-01", "2026-07", 1, 99_000m),
            Rate("org-b", "env-a", "WC-01", "2026-06", 1, 88_000m));
        await db.SaveChangesAsync();

        var response = await new ListWorkCenterMachineOverheadRatesQueryHandler(db).Handle(
            new ListWorkCenterMachineOverheadRatesQuery(" org-a ", " env-a ", " WC-01 ", " 2026-06 "),
            CancellationToken.None);

        Assert.Equal(2, response.CurrentRevision);
        Assert.Equal([2, 1], response.Items.Select(x => x.Revision).ToArray());
        Assert.All(response.Items, item => Assert.Equal("2026-06", item.AccountingPeriodCode));
        Assert.Equal(31m, response.Items[0].FixedHourlyRate);
        Assert.Equal(10m, response.Items[0].VariableHourlyRate);
        Assert.Equal(41m, response.Items[0].TotalHourlyRate);
    }

    [Fact]
    public async Task Settlement_rate_resolution_uses_completion_period_and_freezes_the_latest_revision_identity()
    {
        await using var db = CreateDb();
        db.AccountingPeriods.AddRange(
            Period("org-a", "env-a", "2026-06", 6),
            Period("org-a", "env-a", "2026-07", 7));
        db.WorkCenterMachineOverheadRates.AddRange(
            Rate("org-a", "env-a", "WC-01", "2026-06", 1, 30_000m),
            Rate("org-a", "env-a", "WC-01", "2026-06", 2, 31_000m),
            Rate("org-a", "env-a", "WC-01", "2026-07", 1, 99_000m));
        await db.SaveChangesAsync();

        var resolved = await new ResolveWorkCenterMachineOverheadRateForSettlementQueryHandler(db).Handle(
            new ResolveWorkCenterMachineOverheadRateForSettlementQuery(
                " org-a ", " env-a ", " WC-01 ",
                new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero)),
            CancellationToken.None);

        Assert.Equal("2026-06", resolved.AccountingPeriodCode);
        Assert.Equal(2, resolved.Revision);
        Assert.Equal(31m, resolved.FixedHourlyRate);
        Assert.Equal(10m, resolved.VariableHourlyRate);
        Assert.Equal(41m, resolved.TotalHourlyRate);
        Assert.Equal("CNY", resolved.CurrencyCode);
        Assert.False(string.IsNullOrWhiteSpace(resolved.WorkCenterMachineOverheadRateId));
    }

    [Fact]
    public async Task Settlement_rate_resolution_fails_closed_instead_of_falling_back_or_returning_zero()
    {
        await using var db = CreateDb();
        db.AccountingPeriods.AddRange(
            Period("org-a", "env-a", "2026-06", 6),
            Period("org-a", "env-a", "2026-07", 7));
        db.WorkCenterMachineOverheadRates.AddRange(
            Rate("org-a", "env-a", "WC-01", "2026-07", 1, 99_000m),
            Rate("org-b", "env-a", "WC-01", "2026-06", 1, 88_000m),
            Rate("org-a", "env-b", "WC-01", "2026-06", 1, 77_000m),
            Rate("org-a", "env-a", "WC-02", "2026-06", 1, 66_000m));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ResolveWorkCenterMachineOverheadRateForSettlementQueryHandler(db).Handle(
                new ResolveWorkCenterMachineOverheadRateForSettlementQuery(
                    "org-a", "env-a", "WC-01",
                    new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero)),
                CancellationToken.None));

        Assert.Contains("缺少适用或明确不适用的机器制造费用率", exception.Message, StringComparison.Ordinal);
    }

    private static ConfigureWorkCenterMachineOverheadRateCommand ApplicableCommand(
        string accountingPeriodCode,
        decimal fixedBudget) =>
        new(
            "org-a",
            "env-a",
            "WC-01",
            accountingPeriodCode,
            MachineOverheadApplicability.Applicable,
            fixedBudget,
            10_000m,
            1_000m,
            "CNY",
            "user:finance",
            "月度预定分配率",
            ChangedAtUtc);

    private static AccountingPeriod Period(string organizationId, string environmentId, string code, int month) =>
        AccountingPeriod.Open(
            organizationId,
            environmentId,
            code,
            new DateOnly(2026, month, 1),
            new DateOnly(2026, month, DateTime.DaysInMonth(2026, month)));

    private static WorkCenterMachineOverheadRate Rate(
        string organizationId,
        string environmentId,
        string workCenterId,
        string periodCode,
        int revision,
        decimal fixedBudget) =>
        WorkCenterMachineOverheadRate.DefineApplicable(
            organizationId,
            environmentId,
            workCenterId,
            periodCode,
            fixedBudget,
            10_000m,
            1_000m,
            "CNY",
            revision,
            "system:test",
            "test rate",
            ChangedAtUtc);

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-machine-overhead-rate-{Guid.CreateVersion7():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
