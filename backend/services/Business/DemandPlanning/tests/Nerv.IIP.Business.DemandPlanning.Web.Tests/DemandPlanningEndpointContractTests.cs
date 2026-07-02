using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Auth;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Business.DemandPlanning.Web.Endpoints.Planning;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class DemandPlanningEndpointContractTests
{
    [Fact]
    public void DemandPlanning_endpoints_expose_issue_128_routes_permissions_policies_and_operation_ids()
    {
        var contracts = DemandPlanningEndpointContracts.All.ToArray();

        Assert.Equal(13, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/mps" && x.PermissionCode == DemandPlanningPermissionCodes.MpsRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listPlanningMpsBuckets");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/mps" && x.PermissionCode == DemandPlanningPermissionCodes.MpsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createPlanningMpsBucket");
        Assert.Contains(contracts, x => x.HttpMethod == "PUT" && x.Route == "/api/business/v1/planning/mps/{mpsId}" && x.PermissionCode == DemandPlanningPermissionCodes.MpsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "updatePlanningMpsBucket");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/mps/{mpsId}/review" && x.PermissionCode == DemandPlanningPermissionCodes.MpsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "reviewPlanningMpsBucket");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/mps/{mpsId}/release" && x.PermissionCode == DemandPlanningPermissionCodes.MpsRelease && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "releasePlanningMpsBucket");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/demands" && x.PermissionCode == DemandPlanningPermissionCodes.DemandsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "createOrUpdatePlanningDemand");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/demands" && x.PermissionCode == DemandPlanningPermissionCodes.DemandsRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listPlanningDemands");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/demands/{demandSourceId}/cancel" && x.PermissionCode == DemandPlanningPermissionCodes.DemandsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "cancelPlanningDemand");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/mrp-runs" && x.PermissionCode == DemandPlanningPermissionCodes.MrpRun && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "runPlanningMrp");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/mrp-runs" && x.PermissionCode == DemandPlanningPermissionCodes.MrpRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listPlanningMrpRuns");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/mrp-runs/{runId}/pegging" && x.PermissionCode == DemandPlanningPermissionCodes.MrpRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "getPlanningMrpPegging");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/planning/suggestions" && x.PermissionCode == DemandPlanningPermissionCodes.MrpRead && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "listPlanningSuggestions");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/planning/suggestions/{suggestionId}/accept" && x.PermissionCode == DemandPlanningPermissionCodes.SuggestionsManage && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name && x.OperationId == "acceptPlanningSuggestion");
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
    [InlineData(typeof(RunMrpEndpoint))]
    [InlineData(typeof(ListMrpRunsEndpoint))]
    [InlineData(typeof(ListMrpPeggingEndpoint))]
    [InlineData(typeof(ListPlanningSuggestionsEndpoint))]
    [InlineData(typeof(AcceptPlanningSuggestionEndpoint))]
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
        var handler = new RunMrpCommandHandler(dbContext, new DemandPlanningFixtureInputSnapshotProvider(dbContext));

        var result = await handler.Handle(new RunMrpCommand("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30)), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(2, result.SuggestionCount);
        var suggestions = await new ListPlanningSuggestionsQueryHandler(dbContext).Handle(new ListPlanningSuggestionsQuery("org-001", "env-dev", null), CancellationToken.None);
        Assert.Contains(suggestions, x => x.SuggestionType == "planned-work-order" && x.SkuCode == "SKU-FG-1000" && x.Quantity == 8m);
        Assert.Contains(suggestions, x => x.SuggestionType == "planned-purchase" && x.SkuCode == "SKU-RM-1000" && x.Quantity == 19m);
        var pegging = await new ListMrpPeggingQueryHandler(dbContext).Handle(new ListMrpPeggingQuery(result.RunId), CancellationToken.None);
        Assert.Contains(pegging, x => x.DemandSourceReference == "DEMAND-001" && x.ProductionVersionReference == "PV-001" && x.ManufacturingBomReference == "MBOM-001");
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
        var handler = new RunMrpCommandHandler(
            dbContext,
            new FixedPlanningInputSnapshotProvider(inventorySnapshotSource));

        var result = await handler.Handle(
            new RunMrpCommand("org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

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
        var handler = new RunMrpCommandHandler(
            dbContext,
            new FixedPlanningInputSnapshotProvider(
                "inventory-http:1",
                [
                    new DemandSnapshot("mps:mps-001", "SKU-FG-1000", "pcs", "SITE-01", 12m, new DateOnly(2026, 6, 10), "mps"),
                    new DemandSnapshot("SO-1001", "SKU-FG-1000", "pcs", "SITE-01", 5m, new DateOnly(2026, 6, 12), "sales-order"),
                    new DemandSnapshot("FC-2026-W24", "SKU-FG-2000", "pcs", "SITE-01", 8m, new DateOnly(2026, 6, 14), "forecast"),
                    new DemandSnapshot("SS-SKU-RM-1000-SITE-01", "SKU-RM-1000", "pcs", "SITE-01", 3m, new DateOnly(2026, 6, 20), "safety-stock"),
                ]));

        var result = await handler.Handle(
            new RunMrpCommand("org-001", "env-dev", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

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
