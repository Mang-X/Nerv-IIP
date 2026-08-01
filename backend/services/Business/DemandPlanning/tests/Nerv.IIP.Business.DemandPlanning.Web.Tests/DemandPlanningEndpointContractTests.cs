using System.Net;
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
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Auth;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Business.DemandPlanning.Web.Endpoints.Planning;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class DemandPlanningEndpointContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DemandPlanning_endpoints_expose_issue_128_routes_permissions_policies_and_operation_ids()
    {
        var contracts = DemandPlanningEndpointContracts.All.ToArray();

        Assert.Equal(16, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/mps" && x.PermissionCode == DemandPlanningPermissionCodes.MpsRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listPlanningMpsBuckets");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/mps" && x.PermissionCode == DemandPlanningPermissionCodes.MpsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createPlanningMpsBucket");
        Assert.Contains(contracts, x => x.HttpMethod == "PUT" && x.Route == "/api/business/v1/planning/mps/{mpsId}" && x.PermissionCode == DemandPlanningPermissionCodes.MpsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "updatePlanningMpsBucket");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/mps/{mpsId}/review" && x.PermissionCode == DemandPlanningPermissionCodes.MpsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "reviewPlanningMpsBucket");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/mps/{mpsId}/release" && x.PermissionCode == DemandPlanningPermissionCodes.MpsRelease && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "releasePlanningMpsBucket");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/demands" && x.PermissionCode == DemandPlanningPermissionCodes.DemandsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createOrUpdatePlanningDemand");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/demands" && x.PermissionCode == DemandPlanningPermissionCodes.DemandsRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listPlanningDemands");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/demands/{demandSourceId}/cancel" && x.PermissionCode == DemandPlanningPermissionCodes.DemandsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "cancelPlanningDemand");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/forecasts" && x.PermissionCode == DemandPlanningPermissionCodes.DemandsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createOrUpdatePlanningForecast");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/forecasts" && x.PermissionCode == DemandPlanningPermissionCodes.DemandsRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listPlanningForecasts");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/mrp-runs" && x.PermissionCode == DemandPlanningPermissionCodes.MrpRun && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "runPlanningMrp");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/mrp-runs" && x.PermissionCode == DemandPlanningPermissionCodes.MrpRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listPlanningMrpRuns");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/mrp-runs/{runId}/pegging" && x.PermissionCode == DemandPlanningPermissionCodes.MrpRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "getPlanningMrpPegging");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/suggestions" && x.PermissionCode == DemandPlanningPermissionCodes.MrpRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listPlanningSuggestions");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/suggestions/{suggestionId}/accept" && x.PermissionCode == DemandPlanningPermissionCodes.SuggestionsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "acceptPlanningSuggestion");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/suggestions/{suggestionId}/reject" && x.PermissionCode == DemandPlanningPermissionCodes.SuggestionsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "rejectPlanningSuggestion");
    }

    [Theory]
    [InlineData(typeof(CreateOrUpdateDemandSourceEndpoint))]
    [InlineData(typeof(ListMasterProductionScheduleBucketsEndpoint))]
    [InlineData(typeof(CreateMasterProductionScheduleBucketEndpoint))]
    [InlineData(typeof(UpdateMasterProductionScheduleBucketEndpoint))]
    [InlineData(typeof(ReviewMasterProductionScheduleBucketEndpoint))]
    [InlineData(typeof(ReleaseMasterProductionScheduleBucketEndpoint))]
    [InlineData(typeof(ListDemandSourcesEndpoint))]
    [InlineData(typeof(CancelDemandSourceEndpoint))]
    [InlineData(typeof(CreateOrUpdateForecastInputEndpoint))]
    [InlineData(typeof(ListForecastInputsEndpoint))]
    [InlineData(typeof(RunMrpEndpoint))]
    [InlineData(typeof(ListMrpRunsEndpoint))]
    [InlineData(typeof(ListMrpPeggingEndpoint))]
    [InlineData(typeof(ListPlanningSuggestionsEndpoint))]
    [InlineData(typeof(AcceptPlanningSuggestionEndpoint))]
    [InlineData(typeof(RejectPlanningSuggestionEndpoint))]
    public void DemandPlanning_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType.GetConstructors().Single().GetParameters().Select(x => x.ParameterType).ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task Demand_source_command_creates_and_lists_demand_sources()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(NewDemandCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var demands = await new ListDemandSourcesQueryHandler(dbContext).Handle(new ListDemandSourcesQuery("org-001", "env-dev"), CancellationToken.None);

        Assert.NotEqual(default, id);
        var demand = Assert.Single(demands);
        Assert.Equal("DEMAND-001", demand.SourceReference);
        Assert.Equal(10m, demand.Quantity);
    }

    [Fact]
    public async Task Demand_source_command_normalizes_type_once_before_fingerprint_lookup_and_persistence()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateOrUpdateDemandSourceCommandHandler(dbContext);
        var command = NewDemandCommand() with { DemandType = " Manual ", IdempotencyKey = "normalized-manual" };

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var replay = await handler.Handle(command with { DemandType = "manual" }, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal("manual", Assert.Single(dbContext.DemandSources).DemandType);
    }

    [Fact]
    public async Task Cancel_demand_source_command_removes_source_from_planning_input()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(NewDemandCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new CancelDemandSourceCommandHandler(dbContext).Handle(
            new CancelDemandSourceCommand("org-001", "env-dev", id),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var demands = await new ListDemandSourcesQueryHandler(dbContext)
            .Handle(new ListDemandSourcesQuery("org-001", "env-dev"), CancellationToken.None);
        Assert.Empty(demands);
    }

    [Fact]
    public async Task Mps_bucket_commands_create_update_review_release_and_list_real_status()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateMasterProductionScheduleBucketCommandHandler(dbContext);
        var updateHandler = new UpdateMasterProductionScheduleBucketCommandHandler(dbContext);
        var reviewHandler = new ReviewMasterProductionScheduleBucketCommandHandler(dbContext);
        var releaseHandler = new ReleaseMasterProductionScheduleBucketCommandHandler(dbContext);

        var mpsId = await createHandler.Handle(
            new CreateMasterProductionScheduleBucketCommand(
                "org-001",
                "env-dev",
                "SKU-FG-1000",
                "pcs",
                "SITE-01",
                new DateOnly(2026, 6, 15),
                120m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await updateHandler.Handle(
            new UpdateMasterProductionScheduleBucketCommand(
                "org-001",
                "env-dev",
                mpsId,
                "SKU-FG-1000",
                "pcs",
                "SITE-01",
                new DateOnly(2026, 6, 15),
                132m),
            CancellationToken.None);
        await reviewHandler.Handle(
            new ReviewMasterProductionScheduleBucketCommand("org-001", "env-dev", mpsId, "planner.li"),
            CancellationToken.None);
        await releaseHandler.Handle(
            new ReleaseMasterProductionScheduleBucketCommand("org-001", "env-dev", mpsId, "planning.manager"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var buckets = await new ListMasterProductionScheduleBucketsQueryHandler(dbContext)
            .Handle(new ListMasterProductionScheduleBucketsQuery("org-001", "env-dev", null, null, null, null), CancellationToken.None);

        var bucket = Assert.Single(buckets);
        Assert.Equal(mpsId, bucket.MpsId);
        Assert.Equal("SKU-FG-1000", bucket.SkuCode);
        Assert.Equal(132m, bucket.Quantity);
        Assert.Equal(MasterProductionScheduleStatus.Released, bucket.Status);
        Assert.Equal("planner.li", bucket.ReviewedBy);
        Assert.Equal("planning.manager", bucket.ReleasedBy);
    }

    [Fact]
    public async Task Mps_create_rejects_existing_natural_key_instead_of_upserting_lifecycle_state()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateMasterProductionScheduleBucketCommandHandler(dbContext);
        var command = new CreateMasterProductionScheduleBucketCommand(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 6, 15),
            120m);

        await createHandler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            createHandler.Handle(command, CancellationToken.None));

        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mps_invalid_lifecycle_transitions_are_business_errors()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateMasterProductionScheduleBucketCommandHandler(dbContext);
        var updateHandler = new UpdateMasterProductionScheduleBucketCommandHandler(dbContext);
        var releaseHandler = new ReleaseMasterProductionScheduleBucketCommandHandler(dbContext);
        var mpsId = await createHandler.Handle(
            new CreateMasterProductionScheduleBucketCommand(
                "org-001",
                "env-dev",
                "SKU-FG-1000",
                "pcs",
                "SITE-01",
                new DateOnly(2026, 6, 15),
                120m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var directRelease = await Assert.ThrowsAsync<KnownException>(() =>
            releaseHandler.Handle(
                new ReleaseMasterProductionScheduleBucketCommand("org-001", "env-dev", mpsId, "planning.manager"),
                CancellationToken.None));

        Assert.Contains("reviewed", directRelease.Message, StringComparison.OrdinalIgnoreCase);
        var bucket = await dbContext.MasterProductionSchedules.SingleAsync(x => x.Id == mpsId);
        bucket.MarkReviewed("planner.li");
        bucket.Release("planning.manager");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var updateAfterRelease = await Assert.ThrowsAsync<KnownException>(() =>
            updateHandler.Handle(
                new UpdateMasterProductionScheduleBucketCommand(
                    "org-001",
                    "env-dev",
                    mpsId,
                    "SKU-FG-1000",
                    "pcs",
                    "SITE-01",
                    new DateOnly(2026, 6, 15),
                    132m),
                CancellationToken.None));

        Assert.Contains("cannot be updated", updateAfterRelease.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Demand_source_command_generates_source_reference_and_replays_idempotent_create()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var numbering = new DemandPlanningCodingService();
        var handler = new CreateOrUpdateDemandSourceCommandHandler(dbContext, numbering);
        var command = new CreateOrUpdateDemandSourceCommand(
            "org-001",
            "env-dev",
            "manual",
            null,
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            10m,
            new DateOnly(2026, 6, 1),
            "demand-create-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first, second);
        var demand = Assert.Single(dbContext.DemandSources);
        Assert.Matches("^DEMAND-[0-9]{8}-[0-9]{6}$", demand.SourceReference);
    }

    [Fact]
    public async Task Mrp_run_command_creates_fixture_suggestions_and_pegging()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(NewDemandCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var result = await ExecuteMrpAsync(
            dbContext,
            new DemandPlanningFixtureInputSnapshotProvider(dbContext),
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30));

        Assert.Equal(2, result.SuggestionCount);
        var suggestions = await new ListPlanningSuggestionsQueryHandler(dbContext).Handle(new ListPlanningSuggestionsQuery("org-001", "env-dev", null), CancellationToken.None);
        Assert.Contains(suggestions, x => x.SuggestionType == "planned-work-order" && x.SkuCode == "SKU-FG-1000" && x.Quantity == 8m);
        Assert.Contains(suggestions, x => x.SuggestionType == "planned-purchase" && x.SkuCode == "SKU-RM-1000" && x.Quantity == 19m);
        var pegging = await new ListMrpPeggingQueryHandler(dbContext).Handle(new ListMrpPeggingQuery(result.RunId), CancellationToken.None);
        Assert.Contains(pegging, x => x.DemandSourceReference == "DEMAND-001" && x.ProductionVersionReference == "PV-001" && x.ManufacturingBomReference == "MBOM-001");
    }

    [Fact]
    public async Task Mrp_queries_return_persisted_net_requirement_explanations_and_source_types()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001", "env-dev", "sales-order-id-1001", "SO-1001", "10", "CUST-001",
            "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1), 1));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var result = await ExecuteMrpAsync(
            dbContext,
            new DemandPlanningFixtureInputSnapshotProvider(dbContext),
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30));

        var suggestions = await new ListPlanningSuggestionsQueryHandler(dbContext)
            .Handle(new ListPlanningSuggestionsQuery("org-001", "env-dev", null), CancellationToken.None);
        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        using var suggestionDocument = JsonDocument.Parse(JsonSerializer.Serialize(workOrder, JsonOptions));
        var explanation = suggestionDocument.RootElement.GetProperty("netRequirementExplanation");
        Assert.Equal(10m, explanation.GetProperty("grossDemandQuantity").GetDecimal());
        Assert.Equal(2m, explanation.GetProperty("onHandQuantity").GetDecimal());
        Assert.Equal(8m, explanation.GetProperty("netRequirementQuantity").GetDecimal());
        Assert.Equal("sales", explanation.GetProperty("primarySourceType").GetString());

        var pegging = await new ListMrpPeggingQueryHandler(dbContext).Handle(new ListMrpPeggingQuery(result.RunId), CancellationToken.None);
        var demandPegging = Assert.Single(pegging, x => x.SuggestionId == workOrder.SuggestionId && x.DemandSourceReference == "SO-1001");
        using var peggingDocument = JsonDocument.Parse(JsonSerializer.Serialize(demandPegging, JsonOptions));
        Assert.Equal("sales", peggingDocument.RootElement.GetProperty("sourceType").GetString());
        Assert.Equal(10m, peggingDocument.RootElement.GetProperty("grossDemandQuantity").GetDecimal());
    }

    [Theory]
    [InlineData("inventory-http:2;scheduled-receipts:error;master-data-planning-parameters:none", new[] { "scheduled-receipts" })]
    [InlineData("inventory-http:2;scheduled-receipts:none;master-data-planning-parameters:error", new[] { "master-data-planning-parameters" })]
    [InlineData("inventory-http:2;scheduled-receipts:error;master-data-planning-parameters:error", new[] { "scheduled-receipts", "master-data-planning-parameters" })]
    [InlineData("inventory-http:2;scheduled-receipts:none;master-data-planning-parameters:2", new string[] { })]
    public async Task Mrp_run_command_and_list_query_expose_input_degradation_sources(
        string inventorySnapshotSource,
        string[] expectedSources)
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var result = await ExecuteMrpAsync(
            dbContext,
            new FixedPlanningInputSnapshotProvider(inventorySnapshotSource),
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30));

        Assert.Equal(expectedSources, result.InputDegradationSources);
        var runs = await new ListMrpRunsQueryHandler(dbContext)
            .Handle(new ListMrpRunsQuery("org-001", "env-dev"), CancellationToken.None);
        var run = Assert.Single(runs);
        Assert.Equal(expectedSources, run.InputDegradationSources);
    }

    [Fact]
    public async Task Mrp_run_command_persists_input_sources_and_coverage_period()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var result = await ExecuteMrpAsync(
            dbContext,
            new FixedPlanningInputSnapshotProvider(
                "inventory-http:1",
                [
                    new DemandSnapshot("mps:mps-001", "SKU-FG-1000", "pcs", "SITE-01", 12m, new DateOnly(2026, 6, 10), "mps"),
                    new DemandSnapshot("SO-1001", "SKU-FG-1000", "pcs", "SITE-01", 5m, new DateOnly(2026, 6, 12), "sales-order"),
                    new DemandSnapshot("FC-2026-W24", "SKU-FG-2000", "pcs", "SITE-01", 8m, new DateOnly(2026, 6, 14), "forecast"),
                    new DemandSnapshot("SS-SKU-RM-1000-SITE-01", "SKU-RM-1000", "pcs", "SITE-01", 3m, new DateOnly(2026, 6, 20), "safety-stock"),
                ]),
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30));

        Assert.Equal(["mps", "sales-order", "forecast", "safety-stock"], result.InputSources);
        Assert.Equal(new DateOnly(2026, 6, 10), result.InputCoverageStart);
        Assert.Equal(new DateOnly(2026, 6, 20), result.InputCoverageEnd);
        var run = Assert.Single(await new ListMrpRunsQueryHandler(dbContext)
            .Handle(new ListMrpRunsQuery("org-001", "env-dev"), CancellationToken.None));
        Assert.Equal(["mps", "sales-order", "forecast", "safety-stock"], run.InputSources);
        Assert.Equal(new DateOnly(2026, 6, 10), run.InputCoverageStart);
        Assert.Equal(new DateOnly(2026, 6, 20), run.InputCoverageEnd);
    }

    [Fact]
    public async Task Run_mrp_command_only_registers_queued_run_and_defers_calculation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var runId = await new RunMrpCommandHandler(dbContext)
            .Handle(new RunMrpCommand("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30)), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // 受理只登记排队记录：不拉快照、不产建议、不进入运行态（#1306 受理事务必须秒回）。
        var run = Assert.Single(dbContext.MrpRuns);
        Assert.Equal(runId, run.Id);
        Assert.Equal(MrpRunStatus.Created, run.Status);
        Assert.Null(run.StartedAtUtc);
        Assert.Empty(dbContext.PlanningSuggestions);
    }

    [Fact]
    public async Task Execute_mrp_run_command_rejects_missing_and_non_queued_runs()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var executeHandler = new ExecuteMrpRunCommandHandler(dbContext, new DemandPlanningFixtureInputSnapshotProvider(dbContext));

        var missing = await Assert.ThrowsAsync<KnownException>(() =>
            executeHandler.Handle(new ExecuteMrpRunCommand(new MrpRunId(Guid.CreateVersion7())), CancellationToken.None));
        Assert.Contains("不存在", missing.Message, StringComparison.Ordinal);

        var result = await ExecuteMrpAsync(
            dbContext,
            new DemandPlanningFixtureInputSnapshotProvider(dbContext),
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30));
        var replay = await Assert.ThrowsAsync<KnownException>(() =>
            executeHandler.Handle(new ExecuteMrpRunCommand(result.RunId), CancellationToken.None));
        Assert.Contains("不能重复执行", replay.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mark_mrp_run_failed_command_records_failure_reason_for_run_list()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var runId = await new RunMrpCommandHandler(dbContext)
            .Handle(new RunMrpCommand("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30)), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new MarkMrpRunFailedCommandHandler(dbContext)
            .Handle(new MarkMrpRunFailedCommand(runId, "MRP 计算失败：上游库存快照不可用。"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var run = Assert.Single(await new ListMrpRunsQueryHandler(dbContext)
            .Handle(new ListMrpRunsQuery("org-001", "env-dev"), CancellationToken.None));
        Assert.Equal(MrpRunStatus.Failed, run.Status);
        Assert.Equal("MRP 计算失败：上游库存快照不可用。", run.FailureReason);
    }

    [Fact]
    public async Task Mrp_run_worker_completes_queued_run_end_to_end_and_commits_running_before_calculation()
    {
        // 状态时序断言（PR #1310 审核整改）：worker 必须先在独立事务提交 Running，
        // 再进入计算事务——快照拉取时从新 scope 读 DB 应当已看到 Running。
        var observedStatusesDuringSnapshotFetch = new List<MrpRunStatus>();
        await using var provider = CreateWorkerProvider(sp => new StatusObservingSnapshotProvider(
            new DemandPlanningFixtureInputSnapshotProvider(sp.GetRequiredService<ApplicationDbContext>()),
            sp.GetRequiredService<IServiceScopeFactory>(),
            observedStatusesDuringSnapshotFetch));
        MrpRunId runId;
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(NewDemandCommand(), CancellationToken.None);
            runId = await new RunMrpCommandHandler(dbContext)
                .Handle(new RunMrpCommand("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30)), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            // 受理提交后、worker 启动前：DB 呈现排队态（时序起点）。
            Assert.Equal(MrpRunStatus.Created, dbContext.MrpRuns.AsNoTracking().Single(x => x.Id == runId).Status);
        }

        // 不显式入队：worker 启动恢复扫描必须接管遗留的排队记录（服务重启场景）。
        var worker = CreateWorker(provider);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var run = await WaitForTerminalRunAsync(provider, runId);
            Assert.Equal(MrpRunStatus.Completed, run.Status);
            Assert.NotNull(run.StartedAtUtc);
            Assert.NotNull(run.CompletedAtUtc);
            Assert.True(run.StartedAtUtc <= run.CompletedAtUtc);
            Assert.Equal(2, run.SuggestionCount);
            // 计算事务开始（快照拉取）时，独立 scope 已能读到已提交的 Running。
            Assert.Equal([MrpRunStatus.Running], observedStatusesDuringSnapshotFetch);
            using var scope = provider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, dbContext.PlanningSuggestions.Count());
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Mark_mrp_run_running_command_commits_running_state_and_rejects_replay()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var runId = await new RunMrpCommandHandler(dbContext)
            .Handle(new RunMrpCommand("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30)), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new MarkMrpRunRunningCommandHandler(dbContext);

        await handler.Handle(new MarkMrpRunRunningCommand(runId), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var run = dbContext.MrpRuns.AsNoTracking().Single(x => x.Id == runId);
        Assert.Equal(MrpRunStatus.Running, run.Status);
        Assert.NotNull(run.StartedAtUtc);
        var replay = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(new MarkMrpRunRunningCommand(runId), CancellationToken.None));
        Assert.Contains("不能进入运行中", replay.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mrp_run_worker_marks_run_failed_with_reason_when_snapshot_fetch_throws()
    {
        await using var provider = CreateWorkerProvider(_ => new ThrowingPlanningInputSnapshotProvider());
        var worker = CreateWorker(provider);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            MrpRunId runId;
            using (var scope = provider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                runId = await new RunMrpCommandHandler(dbContext)
                    .Handle(new RunMrpCommand("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30)), CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }

            provider.GetRequiredService<IMrpRunExecutionQueue>().Enqueue(runId);

            var run = await WaitForTerminalRunAsync(provider, runId);
            Assert.Equal(MrpRunStatus.Failed, run.Status);
            Assert.NotNull(run.FailureReason);
            Assert.Contains("MRP 计算失败", run.FailureReason!, StringComparison.Ordinal);
            Assert.Contains("上游库存快照拉取超时", run.FailureReason!, StringComparison.Ordinal);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Mrp_run_worker_recovery_marks_interrupted_running_run_failed()
    {
        await using var provider = CreateWorkerProvider(sp =>
            new DemandPlanningFixtureInputSnapshotProvider(sp.GetRequiredService<ApplicationDbContext>()));
        MrpRunId runId;
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var run = MrpRun.Create("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30));
            run.Start(new PlanningInputSnapshot("product-engineering-http:0", "inventory-http:0", 0, 0));
            dbContext.MrpRuns.Add(run);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            runId = run.Id;
        }

        var worker = CreateWorker(provider);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var run = await WaitForTerminalRunAsync(provider, runId);
            Assert.Equal(MrpRunStatus.Failed, run.Status);
            Assert.Equal(MrpRunWorker.InterruptedFailureReason, run.FailureReason);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static MrpRunWorker CreateWorker(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IMrpRunExecutionQueue>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MrpRunWorker>>());

    private static async Task<MrpRun> WaitForTerminalRunAsync(ServiceProvider provider, MrpRunId runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = provider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var run = await dbContext.MrpRuns.AsNoTracking().SingleAsync(x => x.Id == runId, CancellationToken.None);
            if (run.Status is MrpRunStatus.Completed or MrpRunStatus.Failed)
            {
                return run;
            }

            await Task.Delay(50, CancellationToken.None);
        }

        throw new TimeoutException($"MRP run {runId} did not reach a terminal status in time.");
    }

    private static ServiceProvider CreateWorkerProvider(
        Func<IServiceProvider, IPlanningInputSnapshotProvider> snapshotProviderFactory)
    {
        var services = new ServiceCollection();
        var databaseName = $"demand-planning-worker-{Guid.NewGuid():N}";
        services.AddLogging();
        // worker 经 MediatR 管道执行命令：必须挂 UoW 行为验证「计算事务自动提交」这条真实链路。
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly)
                .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddSingleton<IMrpRunExecutionQueue, MrpRunExecutionQueue>();
        services.AddScoped(snapshotProviderFactory);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 在快照拉取瞬间从**独立 scope** 回读 run 的已提交状态：验证 worker 的 Running
    /// 确实先于计算事务提交（而不是同事务内的未提交中间态）。
    /// </summary>
    private sealed class StatusObservingSnapshotProvider(
        IPlanningInputSnapshotProvider inner,
        IServiceScopeFactory scopeFactory,
        List<MrpRunStatus> observedStatuses) : IPlanningInputSnapshotProvider
    {
        public async Task<PlanningInputSnapshotResult> GetSnapshotAsync(
            string organizationId,
            string environmentId,
            DateOnly horizonStart,
            DateOnly horizonEnd,
            CancellationToken cancellationToken)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                observedStatuses.Add(dbContext.MrpRuns.AsNoTracking().Single().Status);
            }

            return await inner.GetSnapshotAsync(organizationId, environmentId, horizonStart, horizonEnd, cancellationToken);
        }
    }

    private sealed class ThrowingPlanningInputSnapshotProvider : IPlanningInputSnapshotProvider
    {
        public Task<PlanningInputSnapshotResult> GetSnapshotAsync(
            string organizationId,
            string environmentId,
            DateOnly horizonStart,
            DateOnly horizonEnd,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("上游库存快照拉取超时。");
        }
    }

    [Fact]
    public async Task Suggestion_acceptance_is_idempotent_for_same_downstream_reference_and_rejects_conflicts()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), "planned-purchase", "SKU-RM-1000", "pcs", "SITE-01", 19m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 27), "MRP-001");
        dbContext.PlanningSuggestions.Add(suggestion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new AcceptPlanningSuggestionCommandHandler(dbContext);

        await handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "erp", "purchase-request", "PR-001"), CancellationToken.None);
        await handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "erp", "purchase-request", "PR-001"), CancellationToken.None);
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "erp", "purchase-request", "PR-002"), CancellationToken.None));

        Assert.Contains("different downstream", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Purchase_suggestion_acceptance_allows_erp_purchase_requisition_without_caller_known_document_id()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), "planned-purchase", "SKU-RM-1000", "pcs", "SITE-01", 19m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 27), "MRP-001");
        dbContext.PlanningSuggestions.Add(suggestion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var bridge = new CountingPlanningSuggestionDownstreamBridge();
        var handler = new AcceptPlanningSuggestionCommandHandler(dbContext, bridge);

        await handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessErp", "PurchaseRequisition", null), CancellationToken.None);
        await handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessErp", "PurchaseRequisition", null), CancellationToken.None);

        Assert.Equal(PlanningSuggestionStatus.Accepted, suggestion.Status);
        Assert.Equal("BusinessErp", suggestion.AcceptedDownstreamService);
        Assert.Equal("PurchaseRequisition", suggestion.AcceptedDownstreamDocumentType);
        Assert.Equal("PR-SHOULD-BE-CREATED", suggestion.AcceptedDownstreamDocumentId);
        Assert.Equal(1, bridge.CreateCount);
    }

    [Fact]
    public async Task Purchase_suggestion_acceptance_creates_real_erp_requisition_reference_through_bridge()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), "planned-purchase", "SKU-RM-1000", "pcs", "SITE-01", 19m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 27), "MRP-001");
        dbContext.PlanningSuggestions.Add(suggestion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var bridge = new CountingPlanningSuggestionDownstreamBridge();
        var handler = new AcceptPlanningSuggestionCommandHandler(dbContext, bridge);

        await handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessErp", "PurchaseRequisition", null), CancellationToken.None);

        Assert.Equal(PlanningSuggestionStatus.Accepted, suggestion.Status);
        Assert.Equal("BusinessErp", suggestion.AcceptedDownstreamService);
        Assert.Equal("PurchaseRequisition", suggestion.AcceptedDownstreamDocumentType);
        Assert.Equal("PR-SHOULD-BE-CREATED", suggestion.AcceptedDownstreamDocumentId);
        Assert.Equal(1, bridge.CreateCount);
    }

    [Fact]
    public async Task Purchase_suggestion_acceptance_ignores_caller_supplied_erp_requisition_number()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), "planned-purchase", "SKU-RM-1000", "pcs", "SITE-01", 19m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 27), "MRP-001");
        dbContext.PlanningSuggestions.Add(suggestion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new AcceptPlanningSuggestionCommandHandler(dbContext, new CountingPlanningSuggestionDownstreamBridge());

        await handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessErp", "PurchaseRequisition", "PR-CALLER-SHOULD-NOT-WIN"), CancellationToken.None);
        await handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessErp", "PurchaseRequisition", "PR-REPLAY-SHOULD-NOT-CONFLICT"), CancellationToken.None);

        Assert.Equal("BusinessErp", suggestion.AcceptedDownstreamService);
        Assert.Equal("PurchaseRequisition", suggestion.AcceptedDownstreamDocumentType);
        Assert.Equal("PR-SHOULD-BE-CREATED", suggestion.AcceptedDownstreamDocumentId);
    }

    [Fact]
    public async Task Work_order_suggestion_acceptance_is_idempotent_when_replay_omits_downstream_document_id()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), "planned-work-order", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 27), "MRP-001");
        dbContext.PlanningSuggestions.Add(suggestion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var bridge = new CountingPlanningSuggestionDownstreamBridge();
        var handler = new AcceptPlanningSuggestionCommandHandler(dbContext, bridge);

        await handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessMes", "WorkOrder", null), CancellationToken.None);
        await handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessMes", "WorkOrder", null), CancellationToken.None);

        Assert.Equal(PlanningSuggestionStatus.Accepted, suggestion.Status);
        Assert.Equal("BusinessMes", suggestion.AcceptedDownstreamService);
        Assert.Equal("WorkOrder", suggestion.AcceptedDownstreamDocumentType);
        Assert.Equal("WO-SHOULD-NOT-BE-CREATED", suggestion.AcceptedDownstreamDocumentId);
        Assert.Equal(1, bridge.CreateCount);
    }

    [Fact]
    public async Task Suggestion_acceptance_rejects_non_open_suggestion_before_downstream_creation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), "planned-work-order", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 27), "MRP-001");
        suggestion.Reject("planner", "obsolete");
        dbContext.PlanningSuggestions.Add(suggestion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var bridge = new CountingPlanningSuggestionDownstreamBridge();
        var handler = new AcceptPlanningSuggestionCommandHandler(dbContext, bridge);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(new AcceptPlanningSuggestionCommand(suggestion.Id, "BusinessMes", "WorkOrder", null), CancellationToken.None));

        Assert.Contains("Only open planning suggestions can be accepted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, bridge.CreateCount);
    }

    [Fact]
    public async Task Suggestion_rejection_marks_open_suggestion_rejected_and_records_reason()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), "planned-purchase", "SKU-RM-1000", "pcs", "SITE-01", 19m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 27), "MRP-001");
        dbContext.PlanningSuggestions.Add(suggestion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new RejectPlanningSuggestionCommandHandler(dbContext);

        await handler.Handle(new RejectPlanningSuggestionCommand(suggestion.Id, "planner.li", "demand-cancelled"), CancellationToken.None);

        Assert.Equal(PlanningSuggestionStatus.Rejected, suggestion.Status);
        Assert.Equal("demand-cancelled", suggestion.ReasonCode);
    }

    [Fact]
    public async Task Suggestion_rejection_replay_is_tolerated_and_preserves_original_reason()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), "planned-purchase", "SKU-RM-1000", "pcs", "SITE-01", 19m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 27), "MRP-001");
        dbContext.PlanningSuggestions.Add(suggestion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new RejectPlanningSuggestionCommandHandler(dbContext);

        await handler.Handle(new RejectPlanningSuggestionCommand(suggestion.Id, "planner.li", "demand-cancelled"), CancellationToken.None);
        await handler.Handle(new RejectPlanningSuggestionCommand(suggestion.Id, "planner.li", "replayed-reason"), CancellationToken.None);

        Assert.Equal(PlanningSuggestionStatus.Rejected, suggestion.Status);
        Assert.Equal("demand-cancelled", suggestion.ReasonCode);
    }

    [Fact]
    public async Task Suggestion_rejection_of_accepted_suggestion_is_a_business_error()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suggestion = PlanningSuggestion.Create("org-001", "env-dev", new(Guid.CreateVersion7()), "planned-purchase", "SKU-RM-1000", "pcs", "SITE-01", 19m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 27), "MRP-001");
        suggestion.Accept("erp", "purchase-request", "PR-001");
        dbContext.PlanningSuggestions.Add(suggestion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new RejectPlanningSuggestionCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            handler.Handle(new RejectPlanningSuggestionCommand(suggestion.Id, "planner.li", "too-late"), CancellationToken.None));

        Assert.Contains("Only open planning suggestions can be rejected", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PlanningSuggestionStatus.Accepted, suggestion.Status);
    }

    [Fact]
    public void Suggestion_rejection_command_requires_actor_and_reason()
    {
        var validator = new RejectPlanningSuggestionCommandValidator();

        var missingReason = validator.Validate(new RejectPlanningSuggestionCommand(new(Guid.CreateVersion7()), "planner.li", ""));
        var missingActor = validator.Validate(new RejectPlanningSuggestionCommand(new(Guid.CreateVersion7()), "", "demand-cancelled"));
        var valid = validator.Validate(new RejectPlanningSuggestionCommand(new(Guid.CreateVersion7()), "planner.li", "demand-cancelled"));

        Assert.False(missingReason.IsValid);
        Assert.Contains(missingReason.Errors, x => string.Equals(x.PropertyName, nameof(RejectPlanningSuggestionCommand.Reason), StringComparison.OrdinalIgnoreCase));
        Assert.False(missingActor.IsValid);
        Assert.Contains(missingActor.Errors, x => string.Equals(x.PropertyName, nameof(RejectPlanningSuggestionCommand.RejectedBy), StringComparison.OrdinalIgnoreCase));
        Assert.True(valid.IsValid);
    }

    [Fact]
    public async Task DemandPlanning_http_endpoints_reject_anonymous_callers_before_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                ConfigureRequiredUpstreamBaseUrls(builder);
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/business/v1/planning/demands", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            demandType = "manual",
            sourceReference = "DEMAND-001",
            skuCode = "SKU-FG-1000",
            uomCode = "pcs",
            siteCode = "SITE-01",
            quantity = 10m,
            dueDate = "2026-06-01",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DemandPlanning_authorized_http_write_endpoints_execute_command_pipeline()
    {
        await using var factory = new DemandPlanningLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/planning/demands", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            demandType = "manual",
            sourceReference = "DEMAND-HTTP-001",
            skuCode = "SKU-FG-1000",
            uomCode = "pcs",
            siteCode = "SITE-01",
            quantity = 10m,
            dueDate = "2026-06-01",
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected DemandPlanning demand write endpoint to execute, got {(int)response.StatusCode}: {body}");
    }

    /// <summary>
    /// 走异步任务模式的两跳（受理登记 + 后台执行），等价于旧同步 RunMrp 的完整结果，
    /// 供既有计算断言复用。两跳各自 SaveChanges，模拟受理事务与计算事务分离。
    /// </summary>
    private static async Task<ExecuteMrpRunCommandResult> ExecuteMrpAsync(
        ApplicationDbContext dbContext,
        IPlanningInputSnapshotProvider snapshotProvider,
        DateOnly horizonStart,
        DateOnly horizonEnd)
    {
        var runId = await new RunMrpCommandHandler(dbContext)
            .Handle(new RunMrpCommand("org-001", "env-dev", horizonStart, horizonEnd), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var result = await new ExecuteMrpRunCommandHandler(dbContext, snapshotProvider)
            .Handle(new ExecuteMrpRunCommand(runId), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return result;
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"demand-planning-api-contract-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static CreateOrUpdateDemandSourceCommand NewDemandCommand()
    {
        return new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1));
    }

    private sealed class DemandPlanningLiveHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"demand-planning-live-http-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            ConfigureRequiredUpstreamBaseUrls(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IIntegrationEventPublisher>();
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

    private static void ConfigureRequiredUpstreamBaseUrls(IWebHostBuilder builder)
    {
        builder.UseSetting("MasterData:BaseUrl", "http://master-data.local");
        builder.UseSetting("ProductEngineering:BaseUrl", "http://product-engineering.local");
        builder.UseSetting("Inventory:BaseUrl", "http://inventory.local");
        builder.UseSetting("Erp:BaseUrl", "http://erp.local");
        builder.UseSetting("Mes:BaseUrl", "http://mes.local");
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CountingPlanningSuggestionDownstreamBridge : IPlanningSuggestionDownstreamBridge
    {
        public int CreateCount { get; private set; }

        public Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
            PlanningSuggestion suggestion,
            PlanningSuggestionDownstreamRequest request,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            var referenceId = string.Equals(request.DownstreamService, "BusinessErp", StringComparison.OrdinalIgnoreCase)
                ? "PR-SHOULD-BE-CREATED"
                : "WO-SHOULD-NOT-BE-CREATED";
            return Task.FromResult(new PlanningSuggestionDownstreamReference(
                request.DownstreamService,
                request.DownstreamDocumentType,
                referenceId));
        }
    }

    private sealed class FixedPlanningInputSnapshotProvider(
        string inventorySnapshotSource,
        IReadOnlyCollection<DemandSnapshot>? demands = null) : IPlanningInputSnapshotProvider
    {
        public Task<PlanningInputSnapshotResult> GetSnapshotAsync(
            string organizationId,
            string environmentId,
            DateOnly horizonStart,
            DateOnly horizonEnd,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlanningInputSnapshotResult(
                "product-engineering-http:0",
                inventorySnapshotSource,
                demands ?? [],
                [],
                [],
                [],
                [],
                [],
                []));
        }
    }
}
