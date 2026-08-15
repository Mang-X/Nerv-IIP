using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Business.IndustrialTelemetry.Domain;
using Nerv.IIP.Business.Maintenance.Domain;
using Npgsql;
using IndustrialTelemetryDbContext = Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.ApplicationDbContext;
using MaintenanceDbContext = Nerv.IIP.Business.Maintenance.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(AcceptancePostgresLaneDatabase.CollectionName)]
public sealed class RuntimeHoursMaintenancePostgresAcceptanceTests
{
    [RealPostgresFact]
    public async Task Runtime_hour_interval_generates_pm_work_order_from_industrial_telemetry_runtime_hours_on_postgres()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(IndustrialTelemetryFacts.Schema, MaintenanceFacts.Schema);
        var connectionString = AcceptancePostgresLaneDatabase.ConnectionString;
        await using var industrialTelemetryDbContext = CreateIndustrialTelemetryDbContext(connectionString);
        await using var maintenanceDbContext = CreateMaintenanceDbContext(connectionString);
        AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(industrialTelemetryDbContext);
        AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(maintenanceDbContext);
        // The lane database persists across every test in this member, so EnsureCreatedAsync would see the
        // database already exists (it checks database-level existence, not per-schema table existence) and
        // silently skip creating these two schemas' tables. MigrateAsync actually inspects each schema's own
        // __EFMigrationsHistory (dropped by ResetSchemaAsync above) and creates the tables from a clean state.
        await industrialTelemetryDbContext.Database.MigrateAsync();
        await maintenanceDbContext.Database.MigrateAsync();

        industrialTelemetryDbContext.DeviceStateSnapshots.AddRange(
            DeviceStateSnapshot.Record("org-001", "env-dev", "DEV-CNC-PG", "running", new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), "pg-runtime-001", "SCADA-A", "opc-ua-cell-01"),
            DeviceStateSnapshot.Record("org-001", "env-dev", "DEV-CNC-PG", "stopped", new DateTimeOffset(2026, 7, 1, 3, 0, 0, TimeSpan.Zero), "pg-runtime-002", "SCADA-A", "opc-ua-cell-01"));
        await industrialTelemetryDbContext.SaveChangesAsync();

        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-PG", "PM-RUNTIME-PG", "P30D", new DateOnly(2026, 7, 1), "maintenance", runtimeHourInterval: 2.5m);
        _ = plan.ConsumeDueDates(new DateOnly(2026, 7, 1)).ToArray();
        maintenanceDbContext.MaintenancePlans.Add(plan);
        await maintenanceDbContext.SaveChangesAsync();

        using var httpClient = new HttpClient(new IndustrialTelemetryRuntimeHoursHandler(industrialTelemetryDbContext))
        {
            BaseAddress = new Uri("https://industrial-telemetry.local"),
        };
        var runtimeProvider = new HttpIndustrialTelemetryAssetRuntimeHoursProvider(
            new FixedHttpClientFactory(httpClient),
            tokenProvider: null,
            new ThrowingRuntimeHoursFallbackProvider(),
            NullLogger<HttpIndustrialTelemetryAssetRuntimeHoursProvider>.Instance);
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(
            maintenanceDbContext,
            runtimeProvider,
            NullLogger<GenerateDueMaintenanceWorkOrdersCommandHandler>.Instance);

        var result = await handler.Handle(
            new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 7, 1), "system:pm"),
            CancellationToken.None);
        await maintenanceDbContext.SaveChangesAsync();

        Assert.Equal(1, result.GeneratedCount);
        var workOrder = await maintenanceDbContext.MaintenanceWorkOrders.SingleAsync();
        Assert.Equal("PM-RUNTIME-PG:runtime:2.5:1", workOrder.SourceReferenceId);
        var updatedPlan = await maintenanceDbContext.MaintenancePlans.SingleAsync(x => x.PlanCode == "PM-RUNTIME-PG");
        Assert.Equal(3m, updatedPlan.LastGeneratedRuntimeHours);
        Assert.Equal(5.0m, updatedPlan.NextDueRuntimeHours);
    }

    private static IndustrialTelemetryDbContext CreateIndustrialTelemetryDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<IndustrialTelemetryDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"))
            .Options;
        return new IndustrialTelemetryDbContext(options, new NoopMediator());
    }

    private static MaintenanceDbContext CreateMaintenanceDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "maintenance"))
            .Options;
        return new MaintenanceDbContext(options, new NoopMediator());
    }

    private sealed class IndustrialTelemetryRuntimeHoursHandler(IndustrialTelemetryDbContext dbContext) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri?.Query ?? string.Empty);
            var response = await new QueryRuntimeHoursQueryHandler(dbContext).Handle(
                new QueryRuntimeHoursQuery(
                    Required(query, "organizationId"),
                    Required(query, "environmentId"),
                    Required(query, "deviceAssetId"),
                    DateTimeOffset.Parse(Required(query, "windowStartUtc"), CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(Required(query, "windowEndUtc"), CultureInfo.InvariantCulture)),
                cancellationToken);

            var json = JsonSerializer.Serialize(new { data = response, success = true, message = string.Empty, code = 0 }, JsonOptions);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        private static string Required(Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query, string key)
        {
            return query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.ToString()
                : throw new InvalidOperationException($"Missing runtime-hours query value '{key}'.");
        }
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(HttpIndustrialTelemetryAssetRuntimeHoursProvider.ClientName, name);
            return client;
        }
    }

    private sealed class ThrowingRuntimeHoursFallbackProvider : IAssetRuntimeHoursFallbackProvider
    {
        public Task<AssetRuntimeHoursResult> CalculateFallbackAsync(
            string organizationId,
            string environmentId,
            string deviceAssetId,
            DateTimeOffset windowStartUtc,
            DateTimeOffset windowEndUtc,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Real PostgreSQL acceptance must use IndustrialTelemetry runtime-hours, not fallback runtime.");
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            _ = notification;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            _ = notification;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("No mediator requests are expected in this acceptance test.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("No mediator requests are expected in this acceptance test.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("No mediator requests are expected in this acceptance test.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("No mediator streams are expected in this acceptance test.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("No mediator streams are expected in this acceptance test.");
        }
    }
}

internal sealed class RealPostgresFactAttribute : FactAttribute
{
    public RealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run this real PostgreSQL runtime-hours to Maintenance acceptance test.";
        }
    }
}
