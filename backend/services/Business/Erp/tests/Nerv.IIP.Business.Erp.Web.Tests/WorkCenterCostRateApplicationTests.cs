using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WorkCenterCostRateApplicationTests
{
    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Domain_normalizes_currency_and_preserves_exact_audit_fields()
    {
        var changedAt = new DateTimeOffset(2026, 6, 30, 8, 15, 0, TimeSpan.Zero);

        var rate = WorkCenterCostRate.Define(
            " org-001 ", " env-dev ", " WC-01 ", 58.25m, " cny ", July1, July1.AddMonths(1), 3,
            "user:finance-admin", " July correction ", changedAt);

        Assert.Equal("org-001", rate.OrganizationId);
        Assert.Equal("env-dev", rate.EnvironmentId);
        Assert.Equal("WC-01", rate.WorkCenterId);
        Assert.Equal(58.25m, rate.HourlyRate);
        Assert.Equal("CNY", rate.CurrencyCode);
        Assert.Equal(July1, rate.EffectiveFromUtc);
        Assert.Equal(July1.AddMonths(1), rate.EffectiveToUtc);
        Assert.Equal(3, rate.Revision);
        Assert.Equal("user:finance-admin", rate.ChangedBy);
        Assert.Equal("July correction", rate.Reason);
        Assert.Equal(changedAt, rate.ChangedAtUtc);
    }

    [Theory]
    [InlineData("", "env", "WC", 1, "CNY", "user:a", "reason")]
    [InlineData("org", "", "WC", 1, "CNY", "user:a", "reason")]
    [InlineData("org", "env", "", 1, "CNY", "user:a", "reason")]
    [InlineData("org", "env", "WC", 0, "CNY", "user:a", "reason")]
    [InlineData("org", "env", "WC", 1, "CN", "user:a", "reason")]
    [InlineData("org", "env", "WC", 1, "C1Y", "user:a", "reason")]
    [InlineData("org", "env", "WC", 1, "CNY", "actor-without-kind", "reason")]
    [InlineData("org", "env", "WC", 1, "CNY", "user:a", "")]
    public void Configure_validator_rejects_invalid_business_input(
        string organizationId,
        string environmentId,
        string workCenterId,
        decimal hourlyRate,
        string currencyCode,
        string actor,
        string reason)
    {
        var command = new ConfigureWorkCenterCostRateCommand(
            organizationId, environmentId, workCenterId, hourlyRate, currencyCode,
            July1, null, actor, reason, July1.AddDays(-1));

        Assert.False(new ConfigureWorkCenterCostRateCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void Configure_validator_requires_utc_and_an_exclusive_end_after_start()
    {
        var offsetStart = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.FromHours(8));
        var invalidOffset = new ConfigureWorkCenterCostRateCommand(
            "org", "env", "WC", 1m, "USD", offsetStart, null, "user:a", "reason", July1);
        var invalidEnd = invalidOffset with { EffectiveFromUtc = July1, EffectiveToUtc = July1 };
        var invalidChangedAt = invalidEnd with { EffectiveToUtc = July1.AddDays(1), ChangedAtUtc = offsetStart };

        var validator = new ConfigureWorkCenterCostRateCommandValidator();
        Assert.False(validator.Validate(invalidOffset).IsValid);
        Assert.False(validator.Validate(invalidEnd).IsValid);
        Assert.False(validator.Validate(invalidChangedAt).IsValid);
    }

    [Fact]
    public void Configure_validator_rejects_an_omitted_effective_start()
    {
        var command = new ConfigureWorkCenterCostRateCommand(
            "org", "env", "WC", 1m, "USD", default, null, "user:a", "reason", July1);

        var result = new ConfigureWorkCenterCostRateCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.ErrorCode == "NotEmptyValidator" &&
            error.AttemptedValue is DateTimeOffset attemptedValue &&
            attemptedValue == default);
    }

    [Fact]
    public void Domain_rejects_noncanonical_actor_whitespace()
    {
        Assert.Throws<ArgumentException>(() => WorkCenterCostRate.Define(
            "org", "env", "WC", 1m, "CNY", July1, null, 1,
            "user: finance-admin", "reason", July1));
    }

    [Fact]
    public async Task Configure_assigns_monotonic_revision_inside_each_scope()
    {
        await using var db = CreateDb();
        var handler = new ConfigureWorkCenterCostRateCommandHandler(db, new PostgreSqlWorkCenterCostRateRevisionLock(db));

        await handler.Handle(Command("org-a", "env-a", 40m), CancellationToken.None);
        await handler.Handle(Command(" org-a ", " env-a ", 45m), CancellationToken.None);
        await handler.Handle(Command("org-b", "env-a", 50m), CancellationToken.None);
        await handler.Handle(Command("org-a", "env-b", 55m), CancellationToken.None);
        await db.SaveChangesAsync();

        var primaryScopeRevisions = await db.WorkCenterCostRates
            .Where(x => x.OrganizationId == "org-a" && x.EnvironmentId == "env-a")
            .OrderBy(x => x.Revision).Select(x => x.Revision).ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, primaryScopeRevisions);
        Assert.Equal(1, (await db.WorkCenterCostRates.SingleAsync(x => x.OrganizationId == "org-b")).Revision);
        Assert.Equal(1, (await db.WorkCenterCostRates.SingleAsync(x => x.EnvironmentId == "env-b")).Revision);
    }

    [Fact]
    public async Task Configure_rejects_currency_changes_inside_an_existing_cost_rate_scope()
    {
        await using var db = CreateDb();
        var handler = new ConfigureWorkCenterCostRateCommandHandler(
            db,
            new PostgreSqlWorkCenterCostRateRevisionLock(db));
        await handler.Handle(Command("org-a", "env-a", 40m), CancellationToken.None);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            Command("org-a", "env-a", 45m) with { CurrencyCode = "USD" },
            CancellationToken.None));

        Assert.Contains("currency", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.WorkCenterCostRates.ToListAsync());
    }

    [Fact]
    public async Task Advisory_lock_key_is_stable_for_normalized_scope_and_distinct_across_scope_axes()
    {
        await using var db = CreateDb();
        var subject = new PostgreSqlWorkCenterCostRateRevisionLock(db);
        var baseline = subject.GetLockKey("org-a", "env-a", "WC-01");
        var normalized = subject.GetLockKey(" org-a ", " env-a ", " WC-01 ");

        Assert.Equal(baseline, normalized);
        Assert.NotEqual(baseline, subject.GetLockKey("org-b", "env-a", "WC-01"));
        Assert.NotEqual(baseline, subject.GetLockKey("org-a", "env-b", "WC-01"));
        Assert.NotEqual(baseline, subject.GetLockKey("org-a", "env-a", "WC-02"));
    }

    [Fact]
    public void PostgreSQL_revision_lock_is_registered()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var scope = factory.Services.CreateScope();

        Assert.IsType<PostgreSqlWorkCenterCostRateRevisionLock>(
            scope.ServiceProvider.GetRequiredService<IWorkCenterCostRateRevisionLock>());
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<ITransactionUnitOfWork>());
    }

    [Fact]
    public void Work_center_cost_rate_endpoint_requires_the_registered_time_provider()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var scope = factory.Services.CreateScope();
        Assert.Same(TimeProvider.System, scope.ServiceProvider.GetRequiredService<TimeProvider>());

        var parameter = Assert.Single(
            typeof(ConfigureWorkCenterCostRateEndpoint).GetConstructors().Single().GetParameters(),
            candidate => candidate.ParameterType == typeof(TimeProvider));
        Assert.False(parameter.HasDefaultValue);
    }

    [Fact]
    public async Task PostgreSQL_revision_lock_fails_fast_without_current_transaction()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-contacted;Username=nerv;Password=nerv")
            .Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgreSqlWorkCenterCostRateRevisionLock(db).AcquireAsync("org-a", "env-a", "WC-01", CancellationToken.None));

        Assert.Contains("transaction", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_requires_work_center_and_returns_only_scoped_history_with_current_revision()
    {
        await using var db = CreateDb();
        db.WorkCenterCostRates.AddRange(
            Rate("org-a", "env-a", "WC-01", 1, 40m, July1.AddMonths(-1), null),
            Rate("org-a", "env-a", "WC-01", 2, 45m, July1, null),
            Rate("org-a", "env-a", "WC-02", 1, 99m, July1, null),
            Rate("org-b", "env-a", "WC-01", 1, 88m, July1, null),
            Rate("org-a", "env-b", "WC-01", 1, 77m, July1, null));
        await db.SaveChangesAsync();

        var validator = new ListWorkCenterCostRatesQueryValidator();
        Assert.False(validator.Validate(new ListWorkCenterCostRatesQuery("org-a", "env-a", "", July1)).IsValid);

        var response = await new ListWorkCenterCostRatesQueryHandler(db, new FixedTimeProvider(July1)).Handle(
            new ListWorkCenterCostRatesQuery(" org-a ", " env-a ", " WC-01 ", null), CancellationToken.None);

        Assert.Equal(July1, response.AtUtc);
        Assert.Equal(2, response.CurrentEffectiveRevision);
        Assert.Equal([2, 1], response.Items.Select(x => x.Revision).ToArray());
        Assert.True(response.Items.Single(x => x.Revision == 2).IsEffectiveAtUtc);
        Assert.True(response.Items.Single(x => x.Revision == 2).IsCurrentEffectiveRevision);
        Assert.Equal("effective", response.Items.Single(x => x.Revision == 2).EffectiveStatus);
        Assert.True(response.Items.Single(x => x.Revision == 1).IsEffectiveAtUtc);
        Assert.False(response.Items.Single(x => x.Revision == 1).IsCurrentEffectiveRevision);
        Assert.All(response.Items, item => Assert.Equal("WC-01", item.WorkCenterId));
    }

    [Fact]
    public async Task Labor_cost_uses_highest_active_revision_at_report_occurrence_time()
    {
        await using var db = CreateDb();
        db.WorkCenterCostRates.AddRange(
            Rate("org-001", "env-dev", "WC-01", 1, 40m, July1.AddMonths(-2), null),
            Rate("org-001", "env-dev", "WC-01", 2, 60m, July1.AddMonths(-1), null),
            Rate("org-001", "env-dev", "WC-01", 3, 90m, July1.AddMonths(1), null));
        await db.SaveChangesAsync();

        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
            db, new InMemoryIntegrationEventDeadLetterStore(), db).HandleAsync(Report("event-active", July1), CancellationToken.None);
        await db.SaveChangesAsync();

        var cost = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(120m, cost.LaborCost);
        Assert.Equal(60m, Assert.Single(cost.Details).Rate);
    }

    [Fact]
    public async Task Future_and_expired_rates_fail_closed_without_consuming_inbox()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        db.WorkCenterCostRates.AddRange(
            Rate("org-001", "env-dev", "WC-01", 1, 40m, July1.AddMonths(-2), July1.AddDays(-1)),
            Rate("org-001", "env-dev", "WC-01", 2, 60m, July1.AddDays(1), null));
        await db.SaveChangesAsync();

        await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters, db)
            .HandleAsync(Report("event-no-active", July1), CancellationToken.None);

        Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());
        Assert.Empty(await db.WorkOrderCosts.ToListAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("missing-work-center-cost-rate", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Configure_then_replay_of_the_exact_event_succeeds()
    {
        await using var db = CreateDb();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var report = Report("event-replay", July1);
        var consumer = new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(db, deadLetters, db);

        await consumer.HandleAsync(report, CancellationToken.None);
        Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());

        await new ConfigureWorkCenterCostRateCommandHandler(db, new PostgreSqlWorkCenterCostRateRevisionLock(db))
            .Handle(Command("org-001", "env-dev", 50m), CancellationToken.None);
        await db.SaveChangesAsync();
        await consumer.HandleAsync(report, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Single(await db.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(100m, (await db.WorkOrderCosts.Include(x => x.Details).SingleAsync()).LaborCost);
    }

    [Fact]
    public void Endpoint_contracts_are_exact_and_write_request_has_no_actor()
    {
        Assert.Null(typeof(ConfigureWorkCenterCostRateRequest).GetProperty("Actor"));

        var configure = ErpEndpointContracts.All.Single(x => x.OperationId == "configureErpWorkCenterCostRate");
        Assert.Equal("POST", configure.HttpMethod);
        Assert.Equal("/api/business/v1/erp/finance/work-center-cost-rates", configure.Route);
        Assert.Equal(ErpPermissionCodes.FinanceManage, configure.PermissionCode);
        Assert.Equal(InternalServiceAuthorizationPolicy.Name, configure.AuthorizationPolicy);

        var list = ErpEndpointContracts.All.Single(x => x.OperationId == "listErpWorkCenterCostRates");
        Assert.Equal("GET", list.HttpMethod);
        Assert.Equal(configure.Route, list.Route);
        Assert.Equal(ErpPermissionCodes.FinanceRead, list.PermissionCode);
        Assert.Equal(InternalServiceAuthorizationPolicy.Name, list.AuthorizationPolicy);
    }

    private static ConfigureWorkCenterCostRateCommand Command(string organizationId, string environmentId, decimal rate) =>
        new(organizationId, environmentId, "WC-01", rate, " cny ", July1.AddMonths(-1), null,
            "user:finance-admin", "initial governed rate", July1.AddDays(-1));

    private static WorkCenterCostRate Rate(
        string organizationId,
        string environmentId,
        string workCenterId,
        int revision,
        decimal hourlyRate,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc) =>
        WorkCenterCostRate.Define(organizationId, environmentId, workCenterId, hourlyRate, "CNY",
            effectiveFromUtc, effectiveToUtc, revision, "system:test", "test rate", July1.AddMonths(-3));

    private static ProductionReportRecordedIntegrationEvent Report(string eventId, DateTimeOffset reportedAtUtc) =>
        new(eventId, MesIntegrationEventTypes.ProductionReportRecorded, 1, reportedAtUtc,
            MesIntegrationEventSources.BusinessMes, "RPT-001", "WO-001", "org-001", "env-dev", "operator", eventId,
            new ProductionReportRecordedPayload("RPT-001", "WO-001", "OP-001", "WC-01", null,
                8m, 2m, 0m, "ea", 5m, reportedAtUtc, false, MaterialMovementCount: 0));

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-rate-{Guid.CreateVersion7():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
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
