using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Auth;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionRecords;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityInspectionEndpointContractTests
{
    [Fact]
    public void Inspection_endpoints_expose_issue_132_routes_permissions_and_operation_ids()
    {
        var contracts = QualityInspectionEndpointContracts.All;

        Assert.Equal(6, contracts.Count);
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
    }

    [Theory]
    [InlineData(typeof(CreateInspectionPlanEndpoint))]
    [InlineData(typeof(ActivateInspectionPlanEndpoint))]
    [InlineData(typeof(ListInspectionPlansEndpoint))]
    [InlineData(typeof(CreateInspectionRecordEndpoint))]
    [InlineData(typeof(OpenNcrFromInspectionEndpoint))]
    [InlineData(typeof(ListInspectionRecordsEndpoint))]
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

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"quality-inspection-api-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private sealed class FixedNonconformanceReportCodeGenerator : INonconformanceReportCodeGenerator
    {
        public Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("NCR-INS-001");
        }
    }
}
