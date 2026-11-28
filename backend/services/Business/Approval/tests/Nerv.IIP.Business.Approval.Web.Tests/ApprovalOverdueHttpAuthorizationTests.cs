using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Infrastructure;
using Nerv.IIP.Testing;
using Nerv.IIP.Testing.EntityFramework;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Approval.Web.Tests;

public sealed class ApprovalOverdueHttpAuthorizationTests
{
    private const string Route = "/api/business/v1/approvals/tasks/overdue/check";
    private const string Authorization = "Approval:OverdueCheck:HttpAuthorization:";
    private const string Profile = Authorization + "ScopedCallers:Profiles:0:";
    private const string SharedToken = "synthetic-shared-internal-token";
    private const string OperatorToken = "synthetic-overdue-operator-token";

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task Overdue_http_check_marks_only_the_requested_authorized_scope(
        bool allowLegacy, HttpStatusCode expectedSharedStatus)
    {
        await using var connection = new SqliteConnection($"Data Source=approval-overdue-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await using var factory = CreateHost(allowLegacy, builder => ConfigureDatabase(builder, connection.ConnectionString));
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);

        client.DefaultRequestHeaders.Authorization = new("Bearer", OperatorToken);
        using var wrongOrg = await CheckAsync(client, "org-other", "env-dev");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrg.StatusCode);
        using var wrongEnv = await CheckAsync(client, "org-001", "env-other");
        Assert.Equal(HttpStatusCode.Forbidden, wrongEnv.StatusCode);
        await AssertMarkedCountAsync(client, "org-001", "env-dev", 1);
        await AssertMarkedCountAsync(client, "org-001", "env-dev", 0);
        using var collections = await client.GetAsync("/api/business/v1/approvals/chains?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.Unauthorized, collections.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", SharedToken);
        using var shared = await CheckAsync(client, "org-other", "env-dev");
        Assert.Equal(expectedSharedStatus, shared.StatusCode);
        if (allowLegacy)
        {
            using var json = JsonDocument.Parse(await shared.Content.ReadAsStringAsync());
            Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("markedCount").GetInt32());
        }
    }

    [Theory]
    [InlineData("Subject", "business-gateway")]
    [InlineData("Permissions:0", "internal.approval-chains.list-gateway")]
    public async Task Overdue_http_check_requires_the_operator_subject_and_permission(string key, string value)
    {
        await using var factory = CreateHost(false, builder => builder.UseSetting(Profile + key, value));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", OperatorToken);
        using var response = await CheckAsync(client, "org-001", "env-dev");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Overdue_http_check_rejects_reused_credentials_at_startup(bool reuseSharedBearer)
    {
        await using var factory = CreateHost(false, builder =>
        {
            if (reuseSharedBearer)
            {
                builder.UseSetting(Profile + "BearerToken", SharedToken);
                return;
            }

            const string readProfile = "Approval:ReadCollections:ScopedCallers:Profiles:0:";
            builder.UseSetting(readProfile + "Name", "collection-reader");
            builder.UseSetting(readProfile + "BearerToken", OperatorToken);
            builder.UseSetting(readProfile + "Subject", "business-gateway");
            builder.UseSetting(readProfile + "OrganizationId", "org-001");
            builder.UseSetting(readProfile + "EnvironmentId", "env-dev");
            builder.UseSetting(readProfile + "Permissions:0", "internal.approval-chains.list-gateway");
        });

        var failure = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("Approval overdue HTTP credentials must differ from the shared bearer and other Approval operation credentials.",
            failure.Failures);
    }

    [Fact]
    public async Task Scheduler_marks_its_configured_scope_when_overdue_http_check_is_strict()
    {
        await using var connection = new SqliteConnection($"Data Source=approval-overdue-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await using (var seedHost = CreateHost(false, builder => ConfigureDatabase(builder, connection.ConnectionString)))
        {
            await SeedAsync(seedHost.Services);
        }

        await using var schedulerHost = CreateHost(false, builder =>
        {
            ConfigureDatabase(builder, connection.ConnectionString);
            builder.UseSetting("Approval:OverdueCheck:Enabled", "true");
            builder.UseSetting("Approval:OverdueCheck:Scopes:0:OrganizationId", "org-other");
            builder.UseSetting("Approval:OverdueCheck:Scopes:0:EnvironmentId", "env-dev");
        });

        var services = schedulerHost.Services;
        await Eventually.WaitAsync(
            condition: "the scheduled scope has one marked step",
            observe: async cancellationToken =>
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await db.ApprovalChains
                    .Where(chain => chain.OrganizationId == "org-other" && chain.EnvironmentId == "env-dev")
                    .SelectMany(chain => chain.Steps)
                    .CountAsync(step => step.OverdueNotifiedAtUtc != null, cancellationToken);
            },
            isSatisfied: marked => marked == 1,
            describe: marked => $"marked={marked}",
            options: new EventuallyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(25), []));

        using var client = schedulerHost.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", OperatorToken);
        await AssertMarkedCountAsync(client, "org-001", "env-dev", 1);
    }

    private static void ConfigureDatabase(IWebHostBuilder builder, string connectionString)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString)
                .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>());
            services.AddSingleton<IIntegrationEventPublisher, ApprovalEndpointContractTests.NoopIntegrationEventPublisher>();
            var time = new FakeTimeProvider(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time));
        });
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        foreach (var (organization, environment) in new[]
        {
            ("org-001", "env-dev"), ("org-other", "env-dev"), ("org-001", "env-other"),
        })
        {
            var template = ApprovalEndpointContractTests.NewTemplate(organization, environment);
            db.ApprovalTemplates.Add(template);
            db.ApprovalChains.Add(ApprovalChain.Start(template, ApprovalEndpointContractTests.NewDocument(), "fixture"));
        }

        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> CheckAsync(HttpClient client, string organizationId, string environmentId)
        => client.PostAsJsonAsync(Route, new { organizationId, environmentId });

    private static async Task AssertMarkedCountAsync(HttpClient client, string organizationId, string environmentId, int expected)
    {
        using var response = await CheckAsync(client, organizationId, environmentId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("data").GetProperty("markedCount").GetInt32());
    }

    private static WebApplicationFactory<Program> CreateHost(bool allowLegacy, Action<IWebHostBuilder> configure)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("Approval:Seed:Enabled", "false");
            builder.UseSetting("InternalService:BearerToken", SharedToken);
            builder.UseSetting(Authorization + "AllowLegacyBearer", allowLegacy.ToString());
            builder.UseSetting(Profile + "Name", "overdue-operator");
            builder.UseSetting(Profile + "BearerToken", OperatorToken);
            builder.UseSetting(Profile + "Subject", "approval-overdue-operator");
            builder.UseSetting(Profile + "OrganizationId", "org-001");
            builder.UseSetting(Profile + "EnvironmentId", "env-dev");
            builder.UseSetting(Profile + "Permissions:0", "internal.approval-tasks.check-overdue");
            configure(builder);
        });
}
