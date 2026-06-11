using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OpsPostgresProfileTests
{
    [Fact]
    public async Task Postgres_store_persists_task_attempt_and_audit_records()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var previousProvider = Environment.GetEnvironmentVariable("Persistence__Provider");
        var previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OpsDb");
        Environment.SetEnvironmentVariable("Persistence__Provider", "PostgreSQL");
        Environment.SetEnvironmentVariable("ConnectionStrings__OpsDb", connectionString);

        try
        {
            await using var factory = new WebApplicationFactory<Program>();

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Connector-Host-Id", "connector-host-001");
            client.DefaultRequestHeaders.Add("X-Connector-Secret", "local-connector-secret");
            client.DefaultRequestHeaders.Add("X-Organization-Id", "org-001");
            client.DefaultRequestHeaders.Add("X-Environment-Id", "env-dev");

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.EnsureDeletedAsync();
                var migrationRunner = scope.ServiceProvider.GetRequiredService<OpsDatabaseMigrationRunner>();
                await migrationRunner.MigrateAsync();
            }

            var createdResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", CreateTask("pg-ops-001"));
            createdResponse.EnsureSuccessStatusCode();
            var created = await ReadResponseDataAsync<OperationTaskResponse>(createdResponse);
            Assert.NotNull(created);

            var pendingHttpResponse = await client.GetAsync(
                "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");
            pendingHttpResponse.EnsureSuccessStatusCode();
            var pendingResponse = await ReadResponseDataAsync<PendingOperationTasksResponse>(pendingHttpResponse);
            Assert.NotNull(pendingResponse);
            var pending = Assert.Single(pendingResponse.Items);

            var resultResponse = await client.PostAsJsonAsync("/api/ops/v1/operation-results", Succeeded(created.OperationTaskId, pending.AttemptId));
            resultResponse.EnsureSuccessStatusCode();

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var aggregate = await db.OperationTasks
                    .AsNoTracking()
                    .Include(x => x.Attempts)
                    .Include(x => x.AuditRecords)
                    .SingleAsync(x => x.Id == new OperationTaskId(created.OperationTaskId));
                var task = aggregate.ToDetailFact();

                Assert.Equal("completed", task.Status);
                Assert.Equal(pending.AttemptId, task.CurrentAttemptId);
                Assert.Contains(task.AuditRecords, x => x.Action == "operation.requested");
                Assert.Contains(task.AuditRecords, x => x.Action == "operation.claimed");
                Assert.Contains(task.AuditRecords, x => x.Action == "operation.completed");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", previousProvider);
            Environment.SetEnvironmentVariable("ConnectionStrings__OpsDb", previousConnectionString);
        }
    }

    private static CreateOperationTaskRequest CreateTask(string idempotencyKey)
    {
        return new CreateOperationTaskRequest(
            "org-001",
            "env-dev",
            "demo-api-001",
            "lifecycle.restart",
            idempotencyKey,
            "user-admin",
            "verify postgres ops",
            "corr-pg-ops",
            new Dictionary<string, string>());
    }

    private static OperationResult Succeeded(string operationTaskId, string attemptId)
    {
        var context = new ConnectorRequestContext(
            "1.0",
            "1.0",
            "corr-pg-ops",
            DateTimeOffset.Parse("2026-05-17T00:00:00Z"),
            "org-001",
            "env-dev",
            "connector-host-001");

        return new OperationResult(
            context,
            operationTaskId,
            attemptId,
            "demo-api-001",
            "lifecycle.restart",
            DateTimeOffset.Parse("2026-05-17T00:00:01Z"),
            DateTimeOffset.Parse("2026-05-17T00:00:02Z"),
            "succeeded",
            null,
            new Dictionary<string, string> { ["exitCode"] = "0" });
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
