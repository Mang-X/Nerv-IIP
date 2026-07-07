using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Auth;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Application.Queries.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionTasks;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityInspectionEndpointContractTests
{
    [Fact]
    public void Inspection_endpoints_expose_issue_132_routes_permissions_and_operation_ids()
    {
        var contracts = QualityInspectionEndpointContracts.All;

        Assert.Equal(8, contracts.Count);
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/inspection-plans"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionPlansManage
            && x.OperationId == "createBusinessQualityInspectionPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/inspection-plans/{inspectionPlanId}/activate"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionPlansManage
            && x.OperationId == "activateBusinessQualityInspectionPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/inspection-plans"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionRecordsRead
            && x.OperationId == "listBusinessQualityInspectionPlans");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/inspection-records"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionRecordsCreate
            && x.OperationId == "createBusinessQualityInspectionRecord");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/inspection-records/{inspectionRecordId}/failures/ncr"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "openBusinessQualityNcrFromInspection");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/inspection-records"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionRecordsRead
            && x.OperationId == "listBusinessQualityInspectionRecords");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/inspection-tasks"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionRecordsRead
            && x.OperationId == "listBusinessQualityInspectionTasks");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/inspection-tasks/{inspectionTaskId}/inspection-record"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionRecordsCreate
            && x.OperationId == "createBusinessQualityInspectionRecordFromTask");
    }

    [Fact]
    public void Inspection_business_endpoints_require_internal_service_authorization_policy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var failures = QualityInspectionEndpointContracts.All
            .Where(contract => !HasInternalServicePolicy(endpoints, contract.Route))
            .Select(contract => $"{contract.EndpointType.Name} is missing {InternalServiceAuthorizationPolicy.Name}.")
            .ToArray();

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(typeof(CreateInspectionPlanEndpoint))]
    [InlineData(typeof(ActivateInspectionPlanEndpoint))]
    [InlineData(typeof(ListInspectionPlansEndpoint))]
    [InlineData(typeof(CreateInspectionRecordEndpoint))]
    [InlineData(typeof(OpenNcrFromInspectionEndpoint))]
    [InlineData(typeof(ListInspectionRecordsEndpoint))]
    [InlineData(typeof(ListInspectionTasksEndpoint))]
    [InlineData(typeof(CreateInspectionRecordFromTaskEndpoint))]
    public void Inspection_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
    }

    [Fact]
    public void Create_inspection_plan_validator_rejects_duplicate_characteristic_codes()
    {
        var validator = new CreateInspectionPlanCommandValidator();

        var result = validator.Validate(new CreateInspectionPlanCommand(
            "org-001",
            "env-dev",
            "IQP-RECEIVING-001",
            "receiving",
            "SKU-RM-1000",
            null,
            null,
            null,
            "purchase-receipt",
            [
                new InspectionPlanCharacteristicInput("appearance", "Appearance", "visual", "critical", true, "zero-defect"),
                new InspectionPlanCharacteristicInput(" APPEARANCE ", "Appearance duplicate", "visual", "critical", true, "zero-defect"),
            ]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_inspection_plan_validator_rejects_blank_characteristic_codes()
    {
        var validator = new CreateInspectionPlanCommandValidator();

        var result = validator.Validate(new CreateInspectionPlanCommand(
            "org-001",
            "env-dev",
            "IQP-RECEIVING-001",
            "receiving",
            "SKU-RM-1000",
            null,
            null,
            null,
            "purchase-receipt",
            [
                new InspectionPlanCharacteristicInput(null!, "Appearance", "visual", "critical", true, "zero-defect"),
                new InspectionPlanCharacteristicInput(" ", "COA", "document", "major", true, "certificate-match"),
            ]));

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Create_inspection_record_validator_requires_disposition_reason_for_non_passed_lines()
    {
        var validator = new CreateInspectionRecordCommandValidator();

        var result = validator.Validate(new CreateInspectionRecordCommand(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            10m,
            "BATCH-001",
            null,
            [new InspectionResultLineCommandInput("coa", "mismatch", null, InspectionLineResults.Failed, "wrong-spec", 10m, [])],
            null,
            []));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage.Contains("Disposition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_inspection_record_rejects_plan_from_different_business_scope()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = InspectionPlan.Create(
            "org-other",
            "env-dev",
            "IQP-OTHER-001",
            "receiving",
            "SKU-RM-1000",
            "supplier-001",
            null,
            null,
            "purchase-receipt");
        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");
        plan.Activate();
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CreateInspectionRecordCommandHandler(
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new InspectionTaskRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateInspectionRecordCommand(
                "org-001",
                "env-dev",
                plan.Id,
                "receiving",
                "purchase-receipt",
                "RCV-001",
                "SKU-RM-1000",
                10m,
                "BATCH-001",
                null,
                [new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
                null,
                []),
            CancellationToken.None));

        Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.InspectionRecords);
    }

    [Fact]
    public async Task Create_inspection_record_command_preserves_ad_hoc_stock_release()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateInspectionRecordCommandHandler(
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new InspectionTaskRepository(dbContext));

        var recordId = await handler.Handle(
            new CreateInspectionRecordCommand(
                "org-001",
                "env-dev",
                null,
                "receiving",
                "purchase-receipt",
                "RCV-ADHOC-001",
                "SKU-RM-1000",
                10m,
                "LOT-ADHOC-001",
                "SER-ADHOC-001",
                [new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
                null,
                [],
                new StockReleaseDimensionCommandInput(
                    "kg",
                    "SITE-02",
                    "IQC-STAGE",
                    "quality",
                    "supplier",
                    "supplier-001")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var record = Assert.Single(dbContext.InspectionRecords);
        Assert.Equal(record.Id, recordId);
        Assert.Equal("kg", record.UomCode);
        Assert.Equal("SITE-02", record.SiteCode);
        Assert.Equal("IQC-STAGE", record.LocationCode);
        Assert.Equal("quality", record.SourceQualityStatus);
        Assert.Equal("supplier", record.OwnerType);
        Assert.Equal("supplier-001", record.OwnerId);
    }

    [Fact]
    public async Task Create_receiving_inspection_record_is_idempotent_by_source_document_scope()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateInspectionRecordCommandHandler(
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new InspectionTaskRepository(dbContext));
        var command = new CreateInspectionRecordCommand(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-IDEMPOTENT-001",
            "SKU-RM-1000",
            10m,
            "LOT-IDEMPOTENT-001",
            null,
            [new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
            null,
            []);

        var firstId = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var replayId = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(firstId, replayId);
        Assert.Single(dbContext.InspectionRecords);
    }

    [Fact]
    public async Task Create_receiving_inspection_record_allows_distinct_skus_in_same_source_document()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateInspectionRecordCommandHandler(
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new InspectionTaskRepository(dbContext));
        var first = new CreateInspectionRecordCommand(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-MULTI-SKU-001",
            "SKU-RM-1000",
            10m,
            "LOT-MULTI-SKU-001",
            null,
            [new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
            null,
            []);
        var second = first with { SkuCode = "SKU-RM-2000" };

        var firstId = await handler.Handle(first, CancellationToken.None);
        var secondId = await handler.Handle(second, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, dbContext.InspectionRecords.Count());
    }

    [Fact]
    public async Task Create_receiving_inspection_record_reconciles_source_receipt_sku_and_quantity()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateInspectionRecordCommandHandler(
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new InspectionTaskRepository(dbContext),
            sourceDocumentVerifier: new FixedInspectionSourceDocumentVerifier(
                new InspectionSourceDocumentVerification(true, "SKU-OTHER", 5m)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateInspectionRecordCommand(
                "org-001",
                "env-dev",
                null,
                "receiving",
                "purchase-receipt",
                "RCV-MISMATCH-001",
                "SKU-RM-1000",
                10m,
                "LOT-MISMATCH-001",
                null,
                [new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
                null,
                []),
            CancellationToken.None));

        Assert.Contains("SKU", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.InspectionRecords);
    }

    [Fact]
    public async Task List_inspection_plans_returns_offset_page_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.InspectionPlans.AddRange(
            NewInspectionPlan("IQP-001"),
            NewInspectionPlan("IQP-002"),
            NewInspectionPlan("IQP-003"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListInspectionPlansQueryHandler(dbContext).Handle(
            new ListInspectionPlansQuery("org-001", "env-dev", null, null, null, null, null, Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        Assert.Single(response.Items);
    }

    [Fact]
    public async Task List_inspection_plans_filters_keyword_by_id_or_plan_code_before_paging_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var target = NewInspectionPlan("IQP-TARGET-001");
        dbContext.InspectionPlans.AddRange(target, NewInspectionPlan("IQP-OTHER-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var codeResponse = await new ListInspectionPlansQueryHandler(dbContext).Handle(
            new ListInspectionPlansQuery("org-001", "env-dev", null, null, null, null, null, Keyword: "target", Skip: 0, Take: 1),
            CancellationToken.None);
        var idResponse = await new ListInspectionPlansQueryHandler(dbContext).Handle(
            new ListInspectionPlansQuery("org-001", "env-dev", null, null, null, null, null, Keyword: target.Id.ToString(), Skip: 0, Take: 1),
            CancellationToken.None);

        Assert.Equal(1, codeResponse.Total);
        Assert.Equal("IQP-TARGET-001", Assert.Single(codeResponse.Items).PlanCode);
        Assert.Equal(1, idResponse.Total);
        Assert.Equal(target.Id, Assert.Single(idResponse.Items).InspectionPlanId);
    }

    [Fact]
    public async Task List_inspection_plans_keyword_does_not_match_non_locator_fields()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.InspectionPlans.Add(NewInspectionPlan(
            "IQP-OTHER-001",
            skuCode: "SKU-TARGET-001",
            partnerId: "PARTNER-TARGET",
            workCenterId: "WC-TARGET",
            deviceAssetId: "DEV-TARGET",
            documentType: "DOC-TARGET"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListInspectionPlansQueryHandler(dbContext).Handle(
            new ListInspectionPlansQuery("org-001", "env-dev", null, null, null, null, null, Keyword: "target", Skip: 0, Take: 10),
            CancellationToken.None);

        Assert.Equal(0, response.Total);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task List_ncrs_filters_keyword_by_id_or_code_before_paging_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var target = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-TARGET-001",
            "receiving",
            "RCV-001",
            "SKU-RM-1000",
            1m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        dbContext.NonconformanceReports.AddRange(
            target,
            NonconformanceReport.Open("org-001", "env-dev", "NCR-OTHER-001", "receiving", "RCV-002", "SKU-RM-1000", 1m, "scratch", null, null, []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var idResponse = await new ListNonconformanceReportsQueryHandler(dbContext).Handle(
            new ListNonconformanceReportsQuery("org-001", "env-dev", null, null, null, Keyword: target.Id.ToString(), Skip: 0, Take: 1),
            CancellationToken.None);
        var codeResponse = await new ListNonconformanceReportsQueryHandler(dbContext).Handle(
            new ListNonconformanceReportsQuery("org-001", "env-dev", null, null, null, Keyword: "target", Skip: 0, Take: 1),
            CancellationToken.None);
        var sourceDocumentResponse = await new ListNonconformanceReportsQueryHandler(dbContext).Handle(
            new ListNonconformanceReportsQuery("org-001", "env-dev", null, null, null, Keyword: "RCV-001", Skip: 0, Take: 1),
            CancellationToken.None);

        Assert.Equal(1, idResponse.Total);
        var item = Assert.Single(idResponse.Items);
        Assert.Equal(target.Id, item.NcrId);
        Assert.Equal("NCR-TARGET-001", item.NcrCode);
        Assert.Equal("NCR-TARGET-001", Assert.Single(codeResponse.Items).NcrCode);
        Assert.Equal("RCV-001", Assert.Single(sourceDocumentResponse.Items).SourceDocumentId);
    }

    [Fact]
    public async Task List_ncrs_keyword_does_not_match_non_locator_fields()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.NonconformanceReports.Add(NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-OTHER-001",
            "receiving",
            "RCV-OTHER-001",
            "SKU-TARGET-001",
            1m,
            "target-defect",
            "BATCH-TARGET",
            "SERIAL-TARGET",
            []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListNonconformanceReportsQueryHandler(dbContext).Handle(
            new ListNonconformanceReportsQuery("org-001", "env-dev", null, null, null, Keyword: "target", Skip: 0, Take: 10),
            CancellationToken.None);

        Assert.Equal(0, response.Total);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Open_ncr_from_inspection_links_record_and_preserves_source_document_reference()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            10m,
            "BATCH-001",
            null,
            [InspectionResultLineInput.Fail("coa", "mismatch", "wrong-spec", 10m, ["file-photo-001"])],
            "Supplier certificate mismatch",
            ["file-mrb-001"]);
        dbContext.InspectionRecords.Add(record);
        await dbContext.SaveChangesAsync();

        var handler = new OpenNcrFromInspectionCommandHandler(
            new InspectionRecordRepository(dbContext),
            new NonconformanceReportRepository(dbContext),
            new FixedNonconformanceReportCodeGenerator());

        var ncrId = await handler.Handle(
            new OpenNcrFromInspectionCommand(record.Id, "Supplier certificate mismatch", ["file-photo-001"]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var ncr = Assert.Single(dbContext.NonconformanceReports);
        Assert.Equal(ncr.Id, ncrId);
        Assert.Equal(record.Id, ncr.SourceInspectionRecordId);
        Assert.Equal("RCV-001", ncr.SourceDocumentId);
        Assert.Equal(ncr.Id.ToString(), record.NonconformanceReportId);
    }

    [Fact]
    public async Task Submit_ncr_high_risk_disposition_requires_approved_central_approval_chain()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-APPROVAL-001",
            "receiving",
            "RCV-001",
            "SKU-RM-1000",
            1m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var approvalStatusClient = new FixedApprovalChainStatusClient(false);
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            approvalStatusClient);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new SubmitNonconformanceReportDispositionCommand(
                ncr.Id,
                "scrap",
                "approval-chain-pending",
                [],
                [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]),
            CancellationToken.None));

        Assert.Contains("approval", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("approval-chain-pending", approvalStatusClient.LastChainId);
    }

    [Fact]
    public async Task Submit_ncr_high_risk_disposition_accepts_approved_central_approval_chain()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-APPROVAL-002",
            "receiving",
            "RCV-002",
            "SKU-RM-1000",
            1m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var approvalStatusClient = new FixedApprovalChainStatusClient(true);
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            approvalStatusClient);

        await handler.Handle(
            new SubmitNonconformanceReportDispositionCommand(
                ncr.Id,
                "scrap",
                "approval-chain-approved",
                [],
                [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]),
            CancellationToken.None);

        Assert.Equal("approval-chain-approved", approvalStatusClient.LastChainId);
        Assert.Equal("approval-chain-approved", ncr.DispositionApprovalChainId);
        Assert.Equal("disposition-in-progress", ncr.Status);
    }

    [Fact]
    public async Task Submit_ncr_high_risk_disposition_rejects_approved_chain_for_other_document()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-APPROVAL-003",
            "receiving",
            "RCV-003",
            "SKU-RM-1000",
            1m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var approvalStatusClient = new FixedApprovalChainStatusClient(
            true,
            expectedNcrCode: "NCR-OTHER");
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            approvalStatusClient);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new SubmitNonconformanceReportDispositionCommand(
                ncr.Id,
                "scrap",
                "approval-chain-other-document",
                [],
                [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]),
            CancellationToken.None));

        Assert.Contains("approval", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("NCR-APPROVAL-003", approvalStatusClient.LastNcrCode);
    }

    [Fact]
    public async Task Submit_ncr_sort_and_screen_disposition_does_not_call_central_approval()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-APPROVAL-004",
            "receiving",
            "RCV-004",
            "SKU-RM-1000",
            1m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var approvalStatusClient = new FixedApprovalChainStatusClient(false);
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            approvalStatusClient);

        await handler.Handle(
            new SubmitNonconformanceReportDispositionCommand(
                ncr.Id,
                "sort-and-screen",
                null,
                ["file-screening-result-001"],
                []),
            CancellationToken.None);

        Assert.Null(approvalStatusClient.LastChainId);
        Assert.Equal("sort-and-screen", ncr.DispositionType);
        Assert.Null(ncr.DispositionApprovalChainId);
        Assert.Equal("disposition-in-progress", ncr.Status);
    }

    [Fact]
    public async Task Complete_ncr_scrap_disposition_ignores_partial_inventory_posting_quantity()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-SCRAP-QTY-001",
            "receiving",
            "RCV-SCRAP-QTY-001",
            "SKU-RM-1000",
            10m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        ncr.SubmitDisposition(
            "scrap",
            "approval-chain-approved",
            [],
            [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]);
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CompleteNonconformanceReportInventoryDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new CorrectiveActionRepository(dbContext));

        await handler.Handle(
            new CompleteNonconformanceReportInventoryDispositionCommand(
                ncr.Id,
                "SM-PARTIAL-001",
                "adjustment",
                "blocked",
                -1m),
            CancellationToken.None);

        Assert.Equal("disposition-in-progress", ncr.Status);
        Assert.Null(ncr.ScrapMovementId);
    }

    [Fact]
    public async Task Complete_ncr_scrap_disposition_records_movement_but_waits_for_effective_capa()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-SCRAP-WAIT-CAPA-001",
            "receiving",
            "RCV-SCRAP-WAIT-CAPA-001",
            "SKU-RM-1000",
            10m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        ncr.SubmitDisposition(
            "scrap",
            "approval-chain-approved",
            [],
            [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]);
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CompleteNonconformanceReportInventoryDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new CorrectiveActionRepository(dbContext));

        await handler.Handle(
            new CompleteNonconformanceReportInventoryDispositionCommand(
                ncr.Id,
                "SM-FULL-WAIT-CAPA-001",
                "adjustment",
                "blocked",
                -10m),
            CancellationToken.None);

        Assert.Equal("disposition-in-progress", ncr.Status);
        Assert.Equal("SM-FULL-WAIT-CAPA-001", ncr.ScrapMovementId);
    }

    [Fact]
    public async Task Close_ncr_scrap_disposition_reuses_recorded_scrap_movement_after_capa_is_effective()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-SCRAP-CLOSE-RECORDED-001",
            "receiving",
            "RCV-SCRAP-CLOSE-RECORDED-001",
            "SKU-RM-1000",
            10m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        ncr.SubmitDisposition(
            "scrap",
            "approval-chain-approved",
            [],
            [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]);
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var completionHandler = new CompleteNonconformanceReportInventoryDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new CorrectiveActionRepository(dbContext));
        await completionHandler.Handle(
            new CompleteNonconformanceReportInventoryDispositionCommand(
                ncr.Id,
                "SM-FULL-RECORDED-001",
                "adjustment",
                "blocked",
                -10m),
            CancellationToken.None);
        dbContext.CorrectiveActions.Add(NewEffectiveCapa(ncr, "CAPA-SCRAP-CLOSE-RECORDED-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var closeHandler = new CloseNonconformanceReportCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new CorrectiveActionRepository(dbContext));

        await closeHandler.Handle(
            new CloseNonconformanceReportCommand(ncr.Id, null, null, null),
            CancellationToken.None);

        Assert.Equal("closed", ncr.Status);
        Assert.Equal("SM-FULL-RECORDED-001", ncr.ScrapMovementId);
    }

    [Fact]
    public async Task Capa_effectiveness_redrives_recorded_scrap_ncr_closure_without_replaying_inventory_event()
    {
        const string databaseName = "quality-capa-redrive-uow";
        await using var provider = CreateInMemoryMediatorProvider(databaseName);
        var publisher = provider.GetRequiredService<RecordingIntegrationEventPublisher>();
        NonconformanceReportId ncrId;
        CorrectiveActionId capaId;
        InspectionRecordId inspectionRecordId;

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ncr = NonconformanceReport.Open(
                "org-001",
                "env-dev",
                "NCR-SCRAP-CAPA-UOW-001",
                "receiving",
                "RCV-SCRAP-CAPA-UOW-001",
                "SKU-RM-1000",
                10m,
                "dimension-out-of-spec",
                null,
                null,
                []);
            ncr.SubmitDisposition(
                "scrap",
                "approval-chain-approved",
                [],
                [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]);
            dbContext.NonconformanceReports.Add(ncr);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            ncr.RecordScrapDispositionMovement("SM-FULL-CAPA-UOW-001", -10m);
            var capa = NewCompletedOpenCapa(ncr, "CAPA-SCRAP-UOW-001");
            var inspection = NewPassedInspectionRecord("RCV-SCRAP-CAPA-UOW-VERIFY-001");
            dbContext.CorrectiveActions.Add(capa);
            dbContext.InspectionRecords.Add(inspection);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            ncrId = ncr.Id;
            capaId = capa.Id;
            inspectionRecordId = inspection.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(
                new VerifyCorrectiveActionEffectivenessCommand(
                    capaId,
                    "qa-manager-001",
                    "No recurrence",
                    DateTimeOffset.Parse("2026-07-10T00:00:00Z"),
                    inspectionRecordId),
                CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reloadedNcr = await dbContext.NonconformanceReports.SingleAsync(x => x.Id == ncrId, CancellationToken.None);
            Assert.Equal("closed", reloadedNcr.Status);
            Assert.Equal("SM-FULL-CAPA-UOW-001", reloadedNcr.ScrapMovementId);
        }

        Assert.Single(publisher.Published.OfType<NcrClosedIntegrationEvent>());
        Assert.Single(publisher.Published.OfType<CapaEffectivenessVerifiedIntegrationEvent>());
        Assert.DoesNotContain(publisher.Published, x => x is InventoryMovementRequestedIntegrationEvent);
    }

    [Fact]
    public async Task Capa_lifecycle_redrive_is_noop_for_closed_ncr_and_repeated_commands()
    {
        const string databaseName = "quality-capa-redrive-idempotent";
        await using var provider = CreateInMemoryMediatorProvider(databaseName);
        var publisher = provider.GetRequiredService<RecordingIntegrationEventPublisher>();
        NonconformanceReportId ncrId;
        CorrectiveActionId capaId;
        InspectionRecordId inspectionRecordId;

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ncr = NonconformanceReport.Open(
                "org-001",
                "env-dev",
                "NCR-SCRAP-CAPA-IDEMPOTENT-001",
                "receiving",
                "RCV-SCRAP-CAPA-IDEMPOTENT-001",
                "SKU-RM-1000",
                10m,
                "dimension-out-of-spec",
                null,
                null,
                []);
            ncr.SubmitDisposition(
                "scrap",
                "approval-chain-approved",
                [],
                [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]);
            dbContext.NonconformanceReports.Add(ncr);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            ncr.RecordScrapDispositionMovement("SM-FULL-CAPA-IDEMPOTENT-001", -10m);
            var capa = NewCompletedOpenCapa(ncr, "CAPA-SCRAP-IDEMPOTENT-001");
            var inspection = NewPassedInspectionRecord("RCV-SCRAP-CAPA-IDEMPOTENT-VERIFY-001");
            dbContext.CorrectiveActions.Add(capa);
            dbContext.InspectionRecords.Add(inspection);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            ncrId = ncr.Id;
            capaId = capa.Id;
            inspectionRecordId = inspection.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(
                new VerifyCorrectiveActionEffectivenessCommand(
                    capaId,
                    "qa-manager-001",
                    "No recurrence",
                    DateTimeOffset.Parse("2026-07-10T00:00:00Z"),
                    inspectionRecordId),
                CancellationToken.None);
            await sender.Send(
                new VerifyCorrectiveActionEffectivenessCommand(
                    capaId,
                    "qa-manager-001",
                    "No recurrence",
                    DateTimeOffset.Parse("2026-07-11T00:00:00Z"),
                    inspectionRecordId),
                CancellationToken.None);
            await sender.Send(new CloseCorrectiveActionCommand(capaId, "qa-manager-001"), CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reloadedNcr = await dbContext.NonconformanceReports.SingleAsync(x => x.Id == ncrId, CancellationToken.None);
            Assert.Equal("closed", reloadedNcr.Status);
            Assert.Equal("SM-FULL-CAPA-IDEMPOTENT-001", reloadedNcr.ScrapMovementId);
        }

        Assert.Single(publisher.Published.OfType<NcrClosedIntegrationEvent>());
        Assert.DoesNotContain(publisher.Published, x => x is InventoryMovementRequestedIntegrationEvent);
    }

    [Fact]
    public async Task Complete_ncr_scrap_disposition_closes_when_effective_capa_exists()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-SCRAP-CLOSE-CAPA-001",
            "receiving",
            "RCV-SCRAP-CLOSE-CAPA-001",
            "SKU-RM-1000",
            10m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        ncr.SubmitDisposition(
            "scrap",
            "approval-chain-approved",
            [],
            [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]);
        dbContext.NonconformanceReports.Add(ncr);
        dbContext.CorrectiveActions.Add(NewEffectiveCapa(ncr, "CAPA-SCRAP-CLOSE-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CompleteNonconformanceReportInventoryDispositionCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new CorrectiveActionRepository(dbContext));

        await handler.Handle(
            new CompleteNonconformanceReportInventoryDispositionCommand(
                ncr.Id,
                "SM-FULL-CLOSE-CAPA-001",
                "adjustment",
                "blocked",
                -10m),
            CancellationToken.None);

        Assert.Equal("closed", ncr.Status);
        Assert.Equal("SM-FULL-CLOSE-CAPA-001", ncr.ScrapMovementId);
    }

    [Fact]
    public async Task Close_ncr_scrap_disposition_requires_linked_effective_capa()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-SCRAP-CAPA-001",
            "receiving",
            "RCV-SCRAP-CAPA-001",
            "SKU-RM-1000",
            10m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        ncr.SubmitDisposition(
            "scrap",
            "approval-chain-approved",
            [],
            [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]);
        dbContext.NonconformanceReports.Add(ncr);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CloseNonconformanceReportCommandHandler(
            new NonconformanceReportRepository(dbContext),
            new CorrectiveActionRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CloseNonconformanceReportCommand(
                ncr.Id,
                null,
                "SM-FULL-001",
                null),
            CancellationToken.None));

        Assert.Contains("CAPA", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("disposition-in-progress", ncr.Status);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"quality-inspection-api-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateInMemoryMediatorProvider(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly)
                .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<INonconformanceReportRepository, NonconformanceReportRepository>();
        services.AddScoped<ICorrectiveActionRepository, CorrectiveActionRepository>();
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<IQualityIntegrationEventContextAccessor, FixedQualityIntegrationEventContextAccessor>();
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());
        return services.BuildServiceProvider();
    }

    private static InspectionPlan NewInspectionPlan(
        string planCode,
        string? skuCode = "SKU-RM-1000",
        string? partnerId = "supplier-001",
        string? workCenterId = null,
        string? deviceAssetId = null,
        string? documentType = "purchase-receipt")
    {
        return InspectionPlan.Create(
            "org-001",
            "env-dev",
            planCode,
            "receiving",
            skuCode,
            partnerId,
            workCenterId,
            deviceAssetId,
            documentType);
    }

    private static CorrectiveAction NewEffectiveCapa(NonconformanceReport ncr, string capaCode)
    {
        var capa = NewCompletedOpenCapa(ncr, capaCode);
        capa.VerifyEffectiveness(
            "qa-manager-001",
            "No recurrence",
            DateTimeOffset.Parse("2026-07-10T00:00:00Z"),
            new InspectionRecordId(Guid.CreateVersion7()),
            "passed");
        return capa;
    }

    private static CorrectiveAction NewCompletedOpenCapa(NonconformanceReport ncr, string capaCode)
    {
        var capa = CorrectiveAction.OpenFromNcr(
            ncr.OrganizationId,
            ncr.EnvironmentId,
            capaCode,
            ncr,
            "Root cause confirmed",
            "Contain affected material",
            "qa-engineer-001",
            DateTimeOffset.Parse("2026-06-30T00:00:00Z"));
        capa.AddAction("corrective", "Fix supplier process", "supplier-quality-001", DateTimeOffset.Parse("2026-06-20T00:00:00Z"));
        var action = capa.Actions.Single();
        capa.CompleteAction(action.Id, action.OwnerUserId, DateTimeOffset.Parse("2026-06-21T00:00:00Z"));
        return capa;
    }

    private static InspectionRecord NewPassedInspectionRecord(string sourceDocumentId)
    {
        return InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            sourceDocumentId,
            "SKU-RM-1000",
            10m,
            null,
            null,
            [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
            null,
            []);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_inspection_policy;Username=nerv;Password=nerv",
            ["InternalService:BearerToken"] = "test-internal-service-token",
        };

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(settings));
            });
    }

    private static bool HasInternalServicePolicy(IEnumerable<RouteEndpoint> endpoints, string route)
    {
        return endpoints
            .Where(endpoint => string.Equals(endpoint.RoutePattern.RawText, route, StringComparison.Ordinal))
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Any(authorizeData => string.Equals(authorizeData.Policy, InternalServiceAuthorizationPolicy.Name, StringComparison.Ordinal));
    }

    private sealed class FixedNonconformanceReportCodeGenerator : INonconformanceReportCodeGenerator
    {
        public Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("NCR-INS-001");
        }
    }

    private sealed class FixedApprovalChainStatusClient(bool isApproved, string? expectedNcrCode = null) : IApprovalChainStatusClient
    {
        public string? LastChainId { get; private set; }
        public string? LastNcrCode { get; private set; }

        public Task<bool> IsApprovedForNcrDispositionAsync(
            string chainId,
            string organizationId,
            string environmentId,
            string ncrCode,
            CancellationToken cancellationToken)
        {
            LastChainId = chainId;
            LastNcrCode = ncrCode;
            return Task.FromResult(isApproved
                && (expectedNcrCode is null || string.Equals(expectedNcrCode, ncrCode, StringComparison.Ordinal)));
        }

        public Task<bool> IsApprovedForCapaClosureAsync(
            string chainId,
            string organizationId,
            string environmentId,
            string capaCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(isApproved);
        }
    }

    private sealed class FixedInspectionSourceDocumentVerifier(InspectionSourceDocumentVerification verification)
        : IInspectionSourceDocumentVerifier
    {
        public Task<InspectionSourceDocumentVerification> VerifyAsync(
            string organizationId,
            string environmentId,
            string sourceType,
            string sourceService,
            string sourceDocumentId,
            string skuCode,
            decimal inspectedQuantity,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(verification);
        }
    }

    private sealed class FixedQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext()
        {
            return new QualityIntegrationEventContext(
                "corr-capa-redrive-001",
                "cause-capa-redrive-001",
                "system:business-quality");
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }
}
