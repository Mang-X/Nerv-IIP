using FastEndpoints;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class SchedulingWorkingDraftPostgresTests
{
    // #4142: true-provider migration, new request scopes, user/version isolation, clear and immutable baseline.
    [SchedulingPostgresFact]
    public async Task Migration_and_http_requests_restore_isolated_edits_without_mutating_plans()
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = new DraftHttpFactory();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var baseline = new FiniteCapacityScheduler().Schedule(problem, "draft-plan-a", problem.HorizonStartUtc);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            var migrations = db.Database.GetMigrations().ToArray();
            var index = Array.FindIndex(migrations, x => x.EndsWith("_AddScheduleWorkingDrafts", StringComparison.Ordinal));
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
            db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
                SchedulePlanContractMapper.ToDomainSnapshot(baseline)));
            db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(problem.OrganizationId, problem.EnvironmentId,
                SchedulePlanContractMapper.ToDomainSnapshot(baseline with { PlanId = "draft-plan-b" })));
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        }

        var assignment = baseline.Assignments.First();
        var editedTask = new SchedulingWorkingDraftTaskContract(assignment.AssignmentId, assignment.OrderId,
            assignment.OperationId, "edited-resource", "edited-center", assignment.StartUtc.AddHours(2),
            assignment.EndUtc.AddHours(2), true,
            [new ScheduleAssignmentSegmentContract(assignment.StartUtc.AddHours(2), assignment.EndUtc.AddHours(2))]);
        var state = new SchedulingWorkingDraftStateContract(1,
            [new SchedulingWorkingDraftOrderContract(assignment.OrderId, 2, true, true)],
            [editedTask],
            [new SchedulingWorkingDraftPendingOperationContract("removed:task", assignment.OrderId, assignment.OperationId,
                SchedulingWorkingDraftPendingSource.Removed, "规划员移回待排", true, assignment.AssignmentId, Task: editedTask)]);
        using var userA = CreateClient(factory, "planner-a");
        using var userB = CreateClient(factory, "planner-b");
        await Save(userA, baseline.PlanId, state, problem);
        await Save(userB, baseline.PlanId, state with { Orders = [] }, problem);
        await Save(userA, "draft-plan-b", state with { Tasks = [] }, problem);
        // Same input saved again replaces this user's version, not another row or another baseline.
        state = state with { Orders = [new SchedulingWorkingDraftOrderContract(assignment.OrderId, 5, false, true)] };
        await Save(userA, baseline.PlanId, state, problem);

        // A new HTTP client/request has a new DbContext, and discovery does not require a selected plan.
        using var reopened = CreateClient(factory, "planner-a");
        var discovered = await Read(reopened, problem);
        Assert.Equal(2, discovered.Count);
        Assert.Equal(JsonSerializer.Serialize(state, SchedulingJson.Options),
            JsonSerializer.Serialize(discovered.Single(x => x.PlanId == baseline.PlanId).State, SchedulingJson.Options));
        var otherUser = Assert.Single(await Read(userB, problem));
        Assert.Empty(otherUser.State.Orders);
        // B clears first while A's earlier row still exists. An unscoped FirstOrDefault delete must fail this invariant.
        using var userBCleared = await userB.DeleteAsync($"/api/business/v1/scheduling/plans/{baseline.PlanId}/working-draft?organizationId={problem.OrganizationId}&environmentId={problem.EnvironmentId}");
        userBCleared.EnsureSuccessStatusCode();
        Assert.Empty(await Read(userB, problem, baseline.PlanId));
        var userAAfterOtherUserClear = Assert.Single(await Read(userA, problem, baseline.PlanId));
        Assert.Equal(JsonSerializer.Serialize(state, SchedulingJson.Options),
            JsonSerializer.Serialize(userAAfterOtherUserClear.State, SchedulingJson.Options));
        await Save(userB, baseline.PlanId, state with { Orders = [] }, problem);
        Assert.Single((await Read(userA, problem, baseline.PlanId)));
        Assert.Empty(await Read(userA, problem with { EnvironmentId = "other-env" }));
        Assert.Empty(await Read(userA, problem with { OrganizationId = "other-org" }));
        using var crossScopeSave = await userA.PutAsJsonAsync($"/api/business/v1/scheduling/plans/{baseline.PlanId}/working-draft",
            new SaveScheduleWorkingDraftRequest(baseline.PlanId, "other-org", problem.EnvironmentId, state), SchedulingJson.Options);
        var denied = await crossScopeSave.Content.ReadFromJsonAsync<ResponseData<SchedulingWorkingDraftContract>>(SchedulingJson.Options);
        Assert.False(denied!.Success);

        using var cleared = await userA.DeleteAsync($"/api/business/v1/scheduling/plans/{baseline.PlanId}/working-draft?organizationId={problem.OrganizationId}&environmentId={problem.EnvironmentId}");
        cleared.EnsureSuccessStatusCode();
        using var afterClear = CreateClient(factory, "planner-a");
        Assert.Empty(await Read(afterClear, problem, baseline.PlanId));
        Assert.Equal("draft-plan-b", Assert.Single(await Read(afterClear, problem)).PlanId);
        Assert.Single(await Read(userB, problem));
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unchanged = await verify.SchedulePlans.Include(x => x.Assignments).ThenInclude(x => x.Segments)
            .SingleAsync(x => x.PlanId == baseline.PlanId);
        Assert.Equal(SchedulePlanLifecycleStatus.Generated, unchanged.Status);
        Assert.Null(unchanged.ReleasedAtUtc);
        Assert.Equal(JsonSerializer.Serialize(baseline.Assignments, SchedulingJson.Options),
            JsonSerializer.Serialize(SchedulePlanContractMapper.ToContract(unchanged).Assignments, SchedulingJson.Options));
    }

    private static HttpClient CreateClient(DraftHttpFactory factory, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        client.DefaultRequestHeaders.Add(SchedulingWorkingDraftHeaders.UserId, userId);
        return client;
    }

    private static async Task Save(HttpClient client, string planId, SchedulingWorkingDraftStateContract state, SchedulingProblemContract scope)
    {
        using var response = await client.PutAsJsonAsync($"/api/business/v1/scheduling/plans/{planId}/working-draft",
            new SaveScheduleWorkingDraftRequest(planId, scope.OrganizationId, scope.EnvironmentId, state), SchedulingJson.Options);
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<ResponseData<SchedulingWorkingDraftContract>>(SchedulingJson.Options);
        Assert.True(saved!.Success);
        Assert.Equal(planId, saved.Data.PlanId);
    }

    private static async Task<IReadOnlyList<SchedulingWorkingDraftContract>> Read(HttpClient client, SchedulingProblemContract scope, string? planId = null)
    {
        var response = await client.GetFromJsonAsync<ResponseData<List<SchedulingWorkingDraftContract>>>(
            $"/api/business/v1/scheduling/working-drafts?organizationId={scope.OrganizationId}&environmentId={scope.EnvironmentId}" +
            (planId is null ? "" : $"&planId={planId}"), SchedulingJson.Options);
        Assert.True(response!.Success);
        return response.Data;
    }

    private sealed class DraftHttpFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddFastEndpoints(options =>
                {
                    options.Assemblies = [typeof(Program).Assembly];
                    options.DisableAutoDiscovery = true;
                    options.IncludeAbstractValidators = true;
                });
            });
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.UseSetting("ConnectionStrings:PostgreSQL", SchedulingPostgresLaneDatabase.ConnectionString);
            builder.UseSetting("Persistence:AutoMigrate", "false");
            foreach (var service in new[] { "MasterData", "ProductEngineering", "Mes", "IndustrialTelemetry", "Maintenance" })
                builder.UseSetting($"{service}:BaseUrl", "http://localhost");
        }
    }
}
