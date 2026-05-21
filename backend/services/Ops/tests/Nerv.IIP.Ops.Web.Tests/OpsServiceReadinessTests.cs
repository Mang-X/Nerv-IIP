using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Nerv.IIP.Ops.Web.Tests;

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
            Environment.SetEnvironmentVariable("Persistence__AutoMigrate", "true");
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
    public void Operation_task_id_generation_does_not_count_existing_tasks()
    {
        var repositorySource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "services",
            "Ops",
            "src",
            "Nerv.IIP.Ops.Infrastructure",
            "Repositories",
            "OperationTaskRepository.cs"));

        Assert.DoesNotContain(".CountAsync(", repositorySource, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
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
