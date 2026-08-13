using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class RushWorkOrderHttpPostgresTests
{
    [MesRealPostgresFact]
    public async Task PostgreSQL_http_creation_still_dispatches_work_order_created_after_sku_gate_save()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var recorder = new WorkOrderCreatedRecorder();
        await using var factory = CreateFactory(MesPostgresLaneDatabase.ConnectionString, recorder);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
            await dbContext.Database.MigrateAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-token");
        var response = await client.PostAsJsonAsync("/api/business/v1/mes/work-orders/rush", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-HTTP-PG-001",
            skuId = "SKU-ACTIVE",
            productionVersionId = "PV-001",
            quantity = 5m,
            dueUtc = "2026-07-20T08:00:00Z",
            workCenterId = "WC-001",
            operationTaskId = "OP-001",
            operationSequence = 10,
            durationMinutes = 30,
            idempotencyKey = "rush-http-pg-001",
        });

        response.EnsureSuccessStatusCode();
        var domainEvent = await recorder.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("WO-HTTP-PG-001", domainEvent.WorkOrder.WorkOrderIdValue);
        Assert.Equal(1, recorder.Count);

        using var assertionScope = factory.Services.CreateScope();
        var assertionContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await assertionContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                x.WorkOrderIdValue == "WO-HTTP-PG-001",
            CancellationToken.None));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        WorkOrderCreatedRecorder recorder)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = connectionString,
                    ["Messaging:Provider"] = "InMemory",
                    ["Cap:Version"] = $"test-rush-http-{Guid.CreateVersion7():N}",
                    ["InternalService:BearerToken"] = "test-internal-token",
                };

                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(settings));
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(recorder);
                    services.AddSingleton<INotificationHandler<WorkOrderCreatedDomainEvent>>(
                        serviceProvider => serviceProvider.GetRequiredService<WorkOrderCreatedRecorder>());
                });
            });
    }

    private sealed class WorkOrderCreatedRecorder : INotificationHandler<WorkOrderCreatedDomainEvent>
    {
        private readonly TaskCompletionSource<WorkOrderCreatedDomainEvent> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int count;

        public int Count => Volatile.Read(ref count);

        public Task Handle(WorkOrderCreatedDomainEvent notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            completion.TrySetResult(notification);
            return Task.CompletedTask;
        }

        public Task<WorkOrderCreatedDomainEvent> WaitAsync(TimeSpan timeout) =>
            completion.Task.WaitAsync(timeout);
    }
}
