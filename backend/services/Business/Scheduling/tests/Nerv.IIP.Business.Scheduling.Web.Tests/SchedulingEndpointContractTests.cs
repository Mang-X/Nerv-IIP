using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Auth;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingEndpointContractTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Scheduling_endpoints_expose_issue_206_routes_permissions_policies_and_operation_ids()
    {
        var contracts = SchedulingEndpointContracts.All.ToArray();
        var allowedPermissions = new[]
        {
            SchedulingPermissionCodes.PlansRead,
            SchedulingPermissionCodes.PlansManage,
            SchedulingPermissionCodes.PlansRelease
        };

        Assert.Equal(6, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/plans/preview" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "previewSchedulingPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/plans" && x.PermissionCode == SchedulingPermissionCodes.PlansManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createSchedulingPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/scheduling/plans" && x.PermissionCode == SchedulingPermissionCodes.PlansRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listSchedulingPlans");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/scheduling/plans/{planId}" && x.PermissionCode == SchedulingPermissionCodes.PlansRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "getSchedulingPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/scheduling/plans/{planId}/gantt" && x.PermissionCode == SchedulingPermissionCodes.PlansRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "getSchedulingPlanGantt");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/scheduling/plans/{planId}/release" && x.PermissionCode == SchedulingPermissionCodes.PlansRelease && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "releaseSchedulingPlan");
        Assert.All(contracts, x => Assert.Contains(x.PermissionCode, allowedPermissions));
    }

    [Theory]
    [InlineData(typeof(PreviewSchedulePlanEndpoint))]
    [InlineData(typeof(CreateSchedulePlanEndpoint))]
    [InlineData(typeof(ListSchedulePlansEndpoint))]
    [InlineData(typeof(GetSchedulePlanEndpoint))]
    [InlineData(typeof(GetSchedulePlanGanttEndpoint))]
    [InlineData(typeof(ReleaseSchedulePlanEndpoint))]
    public void Scheduling_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType.GetConstructors().Single().GetParameters().Select(x => x.ParameterType).ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task Preview_returns_deterministic_shock_absorber_plan_without_persisting_release_state()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new PreviewSchedulePlanCommandHandler(new FiniteCapacityScheduler(), new FixedTimeProvider(FixedNow));
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();

        var first = await handler.Handle(new PreviewSchedulePlanCommand(problem), CancellationToken.None);
        var second = await handler.Handle(new PreviewSchedulePlanCommand(problem), CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Preview, first.Status);
        Assert.Equal(first.Assignments.Select(x => (x.OperationId, x.ResourceId, x.StartUtc, x.EndUtc)), second.Assignments.Select(x => (x.OperationId, x.ResourceId, x.StartUtc, x.EndUtc)));
        Assert.NotEmpty(first.GanttItems);
        Assert.False(await dbContext.SchedulePlans.AnyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Create_persists_generated_plan_and_detail_returns_all_persisted_plan_facts()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateSchedulePlanCommandHandler(dbContext, new FiniteCapacityScheduler(), new FixedTimeProvider(FixedNow));
        var detailHandler = new GetSchedulePlanDetailQueryHandler(dbContext);

        var problem = CreateProblemWithUnscheduledOperation();
        var created = await createHandler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var detail = await detailHandler.Handle(
            new GetSchedulePlanDetailQuery(created.PlanId, problem.OrganizationId, problem.EnvironmentId),
            CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Generated, created.Status);
        Assert.Equal(created.PlanId, detail.PlanId);
        Assert.NotEmpty(detail.Assignments);
        Assert.NotEmpty(detail.Conflicts);
        Assert.NotEmpty(detail.UnscheduledOperations);
        Assert.NotEmpty(detail.ResourceLoads);
        Assert.NotEmpty(detail.GanttItems);
        Assert.True(await dbContext.ScheduleProblems.AnyAsync(x => x.ProblemId == created.ProblemId, CancellationToken.None));
        Assert.True(await dbContext.SchedulePlans.AnyAsync(x => x.PlanId == created.PlanId, CancellationToken.None));
    }

    [Fact]
    public async Task Create_is_idempotent_for_same_problem_id_and_same_fingerprint()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateSchedulePlanCommandHandler(dbContext, new FiniteCapacityScheduler(), new FixedTimeProvider(FixedNow));
        var command = new CreateSchedulePlanCommand(ShockAbsorberSchedulingFixture.CreateProblem());

        var first = await createHandler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await createHandler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.PlanId, second.PlanId);
        Assert.Equal(first.ProblemFingerprint, second.ProblemFingerprint);
        Assert.Equal(1, await dbContext.ScheduleProblems.CountAsync(x => x.ProblemId == command.Problem.ProblemId, CancellationToken.None));
        Assert.Equal(1, await dbContext.SchedulePlans.CountAsync(x => x.ProblemId == command.Problem.ProblemId, CancellationToken.None));
    }

    [Fact]
    public async Task Create_scopes_problem_id_idempotency_to_organization_and_environment()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateSchedulePlanCommandHandler(dbContext, new FiniteCapacityScheduler(), new FixedTimeProvider(FixedNow));
        var firstProblem = ShockAbsorberSchedulingFixture.CreateProblem();
        var secondTenantProblem = firstProblem with
        {
            OrganizationId = "org-002",
            EnvironmentId = "env-prod"
        };

        var first = await createHandler.Handle(new CreateSchedulePlanCommand(firstProblem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await createHandler.Handle(new CreateSchedulePlanCommand(secondTenantProblem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.NotEqual(first.PlanId, second.PlanId);
        Assert.Equal(firstProblem.ProblemId, second.ProblemId);
        Assert.Equal(2, await dbContext.ScheduleProblems.CountAsync(x => x.ProblemId == firstProblem.ProblemId, CancellationToken.None));
        Assert.Equal(2, await dbContext.SchedulePlans.CountAsync(x => x.ProblemId == firstProblem.ProblemId, CancellationToken.None));
    }

    [Fact]
    public async Task Create_rejects_same_problem_id_with_different_fingerprint_as_business_conflict()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateSchedulePlanCommandHandler(dbContext, new FiniteCapacityScheduler(), new FixedTimeProvider(FixedNow));
        var firstProblem = ShockAbsorberSchedulingFixture.CreateProblem();
        var changedProblem = firstProblem with
        {
            Orders =
            [
                firstProblem.Orders.First() with
                {
                    Quantity = firstProblem.Orders.First().Quantity + 1
                },
                ..firstProblem.Orders.Skip(1)
            ]
        };

        await createHandler.Handle(new CreateSchedulePlanCommand(firstProblem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            createHandler.Handle(new CreateSchedulePlanCommand(changedProblem), CancellationToken.None));

        Assert.Contains(firstProblem.ProblemId, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, await dbContext.ScheduleProblems.CountAsync(x => x.ProblemId == firstProblem.ProblemId, CancellationToken.None));
        Assert.Equal(1, await dbContext.SchedulePlans.CountAsync(x => x.ProblemId == firstProblem.ProblemId, CancellationToken.None));
    }

    [Fact]
    public async Task Release_changes_status_to_released_and_is_idempotent_for_same_plan()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateSchedulePlanCommandHandler(dbContext, new FiniteCapacityScheduler(), new FixedTimeProvider(FixedNow));
        var releaseHandler = new ReleaseSchedulePlanCommandHandler(dbContext, new FixedTimeProvider(FixedNow.AddHours(2)));

        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var created = await createHandler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var first = await releaseHandler.Handle(new ReleaseSchedulePlanCommand(created.PlanId, problem.OrganizationId, problem.EnvironmentId), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await releaseHandler.Handle(new ReleaseSchedulePlanCommand(created.PlanId, problem.OrganizationId, problem.EnvironmentId), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(SchedulePlanStatusContract.Released, first.Status);
        Assert.Equal(SchedulePlanStatusContract.Released, second.Status);
        Assert.Equal(first.ReleasedAtUtc, second.ReleasedAtUtc);
    }

    [Fact]
    public async Task Detail_gantt_and_release_reject_plan_id_outside_requested_tenant_context()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateSchedulePlanCommandHandler(dbContext, new FiniteCapacityScheduler(), new FixedTimeProvider(FixedNow));
        var detailHandler = new GetSchedulePlanDetailQueryHandler(dbContext);
        var ganttHandler = new GetSchedulePlanGanttQueryHandler(dbContext);
        var releaseHandler = new ReleaseSchedulePlanCommandHandler(dbContext, new FixedTimeProvider(FixedNow.AddHours(2)));

        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var created = await createHandler.Handle(new CreateSchedulePlanCommand(problem), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<KnownException>(() =>
            detailHandler.Handle(new GetSchedulePlanDetailQuery(created.PlanId, "org-other", problem.EnvironmentId), CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() =>
            ganttHandler.Handle(new GetSchedulePlanGanttQuery(created.PlanId, problem.OrganizationId, "env-other"), CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() =>
            releaseHandler.Handle(new ReleaseSchedulePlanCommand(created.PlanId, "org-other", "env-other"), CancellationToken.None));
    }

    [Fact]
    public async Task Scheduling_authorized_http_endpoints_execute_mediator_pipeline()
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var createResponse = await client.PostAsJsonAsync("/api/business/v1/scheduling/plans", new CreateSchedulePlanRequest(ShockAbsorberSchedulingFixture.CreateProblem()));
        createResponse.EnsureSuccessStatusCode();
        var createdEnvelope = await createResponse.Content.ReadFromJsonAsync<ResponseData<SchedulePlanContract>>();
        var created = Assert.IsType<SchedulePlanContract>(createdEnvelope?.Data);

        var contextQuery = "organizationId=org-001&environmentId=prod";
        var detail = await client.GetFromJsonAsync<ResponseData<SchedulePlanContract>>($"/api/business/v1/scheduling/plans/{created.PlanId}?{contextQuery}");
        var gantt = await client.GetFromJsonAsync<ResponseData<IReadOnlyCollection<GanttScheduleItemContract>>>($"/api/business/v1/scheduling/plans/{created.PlanId}/gantt?{contextQuery}");
        var releaseResponse = await client.PostAsync($"/api/business/v1/scheduling/plans/{created.PlanId}/release?{contextQuery}", null);
        releaseResponse.EnsureSuccessStatusCode();
        var releasedEnvelope = await releaseResponse.Content.ReadFromJsonAsync<ResponseData<ReleaseSchedulePlanResponse>>();

        Assert.Equal(created.PlanId, detail?.Data?.PlanId);
        Assert.NotEmpty(gantt?.Data ?? []);
        Assert.Equal(SchedulePlanStatusContract.Released, releasedEnvelope?.Data?.Status);
    }

    [Fact]
    public async Task Scheduling_authorized_http_endpoints_scope_plan_routes_by_requested_context()
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var createResponse = await client.PostAsJsonAsync("/api/business/v1/scheduling/plans", new CreateSchedulePlanRequest(ShockAbsorberSchedulingFixture.CreateProblem()));
        createResponse.EnsureSuccessStatusCode();
        var createdEnvelope = await createResponse.Content.ReadFromJsonAsync<ResponseData<SchedulePlanContract>>();
        var created = Assert.IsType<SchedulePlanContract>(createdEnvelope?.Data);

        var detail = await client.GetAsync($"/api/business/v1/scheduling/plans/{created.PlanId}?organizationId=org-other&environmentId=prod");
        var gantt = await client.GetAsync($"/api/business/v1/scheduling/plans/{created.PlanId}/gantt?organizationId=org-001&environmentId=env-other");
        var release = await client.PostAsync($"/api/business/v1/scheduling/plans/{created.PlanId}/release?organizationId=org-other&environmentId=env-other", null);

        await AssertRejectedKnownExceptionEnvelopeAsync(detail);
        await AssertRejectedKnownExceptionEnvelopeAsync(gantt);
        await AssertRejectedKnownExceptionEnvelopeAsync(release);
    }

    [Theory]
    [MemberData(nameof(AnonymousEndpointRequests))]
    public async Task Scheduling_http_endpoints_reject_anonymous_callers_before_handler_execution(HttpRequestMessage request)
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden,
            $"Expected anonymous request to be rejected before handler execution, got {(int)response.StatusCode}.");
    }

    public static IEnumerable<object[]> AnonymousEndpointRequests()
    {
        yield return [JsonRequest(HttpMethod.Post, "/api/business/v1/scheduling/plans/preview", new PreviewSchedulePlanRequest(ShockAbsorberSchedulingFixture.CreateProblem()))];
        yield return [JsonRequest(HttpMethod.Post, "/api/business/v1/scheduling/plans", new CreateSchedulePlanRequest(ShockAbsorberSchedulingFixture.CreateProblem()))];
        yield return [new HttpRequestMessage(HttpMethod.Get, "/api/business/v1/scheduling/plans?organizationId=org-001&environmentId=prod")];
        yield return [new HttpRequestMessage(HttpMethod.Get, "/api/business/v1/scheduling/plans/plan-missing")];
        yield return [new HttpRequestMessage(HttpMethod.Get, "/api/business/v1/scheduling/plans/plan-missing/gantt")];
        yield return [new HttpRequestMessage(HttpMethod.Post, "/api/business/v1/scheduling/plans/plan-missing/release")];
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"scheduling-api-contract-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static SchedulingProblemContract CreateProblemWithUnscheduledOperation()
    {
        var baseProblem = ShockAbsorberSchedulingFixture.CreateProblem();
        var blockedOperation = new SchedulingOperationContract(
            OperationId: "WO-NO-CAP-001-PAINT",
            OperationSequence: 10,
            PredecessorOperationIds: [],
            DurationMinutes: 30,
            RequiredCapabilityCode: "CAP-PAINT",
            EligibleResourceIds: ["DEV-PAINT-404"],
            PrimaryResourceId: "DEV-PAINT-404",
            EarliestStartUtc: baseProblem.HorizonStartUtc,
            DueUtc: baseProblem.HorizonEndUtc,
            Priority: 1,
            IsRush: false,
            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
            MaterialReadyUtc: baseProblem.HorizonStartUtc,
            QualityBlockReason: null,
            SourceReference: "MES:WO-NO-CAP-001");

        return baseProblem with
        {
            Orders =
            [
                ..baseProblem.Orders,
                new SchedulingOrderContract(
                    OrderId: "WO-NO-CAP-001",
                    SkuCode: "FG-CUSTOM",
                    Quantity: 1,
                    DueUtc: baseProblem.HorizonEndUtc,
                    Priority: 1,
                    IsRush: false,
                    Operations: [blockedOperation])
            ]
        };
    }

    private static HttpRequestMessage JsonRequest<T>(HttpMethod method, string requestUri, T body)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task AssertRejectedKnownExceptionEnvelopeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Schedule plan was not found", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private sealed class SchedulingLiveHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"scheduling-live-http-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options
                        .UseInMemoryDatabase(databaseName)
                        .UseInternalServiceProvider(efServices)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                efServices.Dispose();
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
