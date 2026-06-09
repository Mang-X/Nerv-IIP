using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Testing;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Infrastructure.Repositories;

namespace Nerv.IIP.Ops.Web.Tests;

[CollectionDefinition("readiness", DisableParallelization = true)]
public sealed class ReadinessCollection;

[Collection("readiness")]
public sealed class OpsServiceReadinessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Ops_service_exposes_only_health_in_first_iteration()
    {
        var client = factory.CreateClient();

        Assert.Equal("Healthy", await client.GetStringAsync("/health"));
        Assert.Contains("first-iteration-skeleton", await client.GetStringAsync("/internal/ops/v1/build-info"));
    }

    [Fact]
    public void Postgres_automigrate_is_rejected_outside_development()
    {
        var environment = PreserveEnvironment(
            "Persistence__Provider",
            "Persistence__AutoMigrate",
            "ConnectionStrings__OpsDb");

        try
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", "PostgreSQL");
            Environment.SetEnvironmentVariable("Persistence__AutoMigrate", " true ");
            Environment.SetEnvironmentVariable("ConnectionStrings__OpsDb", "Host=localhost;Database=nerv_iip_ops_guard;Username=nerv;Password=nerv");

            using var guardedFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

            var exception = Assert.Throws<InvalidOperationException>(() => guardedFactory.CreateClient());
            Assert.Contains("Persistence:AutoMigrate=true", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            RestoreEnvironment(environment);
        }
    }

    [Fact]
    public async Task Operation_task_id_generation_does_not_count_existing_tasks()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var context = new ApplicationDbContext(options, mediator: null!);
        var repository = new OperationTaskRepository(context);

        var first = (await repository.NextTaskIdAsync()).Id;
        var second = (await repository.NextTaskIdAsync()).Id;

        Assert.NotEqual(first, second);
        Assert.StartsWith("op-", first, StringComparison.Ordinal);
        Assert.StartsWith("op-", second, StringComparison.Ordinal);
        Assert.Empty(GuidVersionAssertions.Version7GuidSuffixFailures(first, "op-"));
        Assert.Empty(GuidVersionAssertions.Version7GuidSuffixFailures(second, "op-"));
    }

    [Fact]
    public async Task Operation_persistent_child_ids_use_version7_guid_suffixes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var context = new ApplicationDbContext(options, mediator: null!);
        var taskRepository = new OperationTaskRepository(context);
        var templateRepository = new OperationTemplateRepository(context);

        var attemptId = await taskRepository.NextAttemptIdAsync();
        var auditRecordId = await taskRepository.NextAuditRecordIdAsync();
        var templateId = await templateRepository.NextTemplateIdAsync();

        Assert.Empty(GuidVersionAssertions.Version7GuidSuffixFailures(attemptId.Id, "attempt-"));
        Assert.Empty(GuidVersionAssertions.Version7GuidSuffixFailures(auditRecordId.Id, "audit-"));
        Assert.Empty(GuidVersionAssertions.Version7GuidSuffixFailures(templateId.Id, "opt-"));
    }

    private static IReadOnlyDictionary<string, string?> PreserveEnvironment(params string[] names)
    {
        return names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
    }

    private static void RestoreEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var (name, value) in environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

}
