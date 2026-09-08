using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesMaterialOwnershipSnapshotPostgresTests
{
    // NERV-2117: ProviderBehavior + Regression。旧样本来自变更前四字段格式，不能由新序列化器生成。
    private const string LegacyAllocations = """
        [{"SourceSiteCode":"SITE-001","SourceLocationCode":"WH-01","SourceLotNo":"LOT-01","Quantity":4}]
        """;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-08T00:00:00Z");

    [MesRealPostgresFact]
    public async Task Legacy_and_explicit_ownership_survive_persistence_and_document_links_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.GetService<IMigrator>().MigrateAsync("20260905025040_AddMesChangeoverRecords");
            setup.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-01", "FG-01", "PV-01", 4m, 1, Now));
            setup.OperationTasks.Add(OperationTask.Create("org-001", "env-dev", "WO-01", "OP-01",
                OperationTaskLifecycleStatus.InProgress, 10, "WC-01", [], Now, TimeSpan.FromHours(1), null, null));
            foreach (var reportNo in new[] { "PR-OLD", "PR-NEW", "PR-REVERSE" })
            {
                setup.ProductionReports.Add(ProductionReport.Record("org-001", "env-dev", reportNo, "WO-01", "OP-01", 1m, 0m, false, Now));
            }

            setup.MaterialIssueRequests.Add(CreateIssue("MIR-OLD", new MaterialTransferAllocation("SITE-001", "WH-01", "LOT-01", 4m)));
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE mes.material_issue_requests SET source_allocations_json = {LegacyAllocations} WHERE request_no = 'MIR-OLD'");
            // 旧表尚无 owner 列；保留真实历史耗料形状后再执行新增迁移。
            await setup.Database.ExecuteSqlRawAsync("""
                INSERT INTO mes.production_report_material_consumptions
                    (id, organization_id, environment_id, report_no, work_order_id, operation_task_id,
                     material_id, material_lot_id, uom_code, consumed_quantity, material_issue_request_no, site_code, location_code)
                VALUES ('11111111-1111-4111-8111-111111111111', 'org-001', 'env-dev', 'PR-OLD', 'WO-01', 'OP-01',
                        'MAT-01', 'LOT-01', 'KG', 1, 'MIR-OLD', 'SITE-001', 'LINE-01')
                """);
            await setup.Database.MigrateAsync();
        }

        await using (var write = new ApplicationDbContext(options, new NoopMediator()))
        {
            var oldIssue = await write.MaterialIssueRequests.SingleAsync();
            var oldAllocation = Assert.Single(oldIssue.GetSourceAllocations());
            Assert.Equal("production", oldAllocation.OwnerType);
            Assert.Null(oldAllocation.OwnerId);
            Assert.Equal(LegacyAllocations, oldIssue.SourceAllocationsJson);
            var oldConsumption = await write.ProductionReportMaterialConsumptions.SingleAsync();
            Assert.Equal("production", oldConsumption.OwnerType);
            Assert.Null(oldConsumption.OwnerId);
            var movement = new ProductionMaterialConsumedIntegrationEventConverter().Convert(new ProductionMaterialConsumedDomainEvent(oldConsumption));
            Assert.Equal("production", movement.Payload.OwnerType);
            Assert.Null(movement.Payload.OwnerId);
            Assert.Equal(-1m, movement.Payload.Quantity);

            var issue = CreateIssue("MIR-NEW", new MaterialTransferAllocation("SITE-001", "WH-01", "LOT-01", 4m, "company", null));
            write.MaterialIssueRequests.Add(issue);
            var allocation = Assert.Single(issue.GetSourceAllocations());
            var consumption = ProductionReportMaterialConsumption.Record("org-001", "env-dev", "PR-NEW", "WO-01", "OP-01",
                "MAT-01", "LOT-01", "KG", 1m, issue.RequestNo, "SITE-001", "LINE-01", allocation.OwnerType, allocation.OwnerId);
            write.ProductionReportMaterialConsumptions.Add(consumption);
            write.ProductionReportMaterialConsumptions.Add(ProductionReportMaterialConsumption.Reverse(consumption, "PR-REVERSE"));
            await write.SaveChangesAsync();
        }

        await using var read = new ApplicationDbContext(options, new NoopMediator());
        var frozenConsumption = await read.ProductionReportMaterialConsumptions.SingleAsync(x => x.ReportNo == "PR-NEW");
        var supplyingIssue = await read.MaterialIssueRequests.SingleAsync(x =>
            x.OrganizationId == frozenConsumption.OrganizationId && x.EnvironmentId == frozenConsumption.EnvironmentId &&
            x.RequestNo == frozenConsumption.MaterialIssueRequestNo);
        var frozenAllocation = Assert.Single(supplyingIssue.RequireTransferLocations().SourceAllocations);
        Assert.Equal("company", frozenAllocation.OwnerType);
        Assert.Null(frozenAllocation.OwnerId);
        Assert.Equal(frozenAllocation.OwnerType, frozenConsumption.OwnerType);
        Assert.Equal(frozenAllocation.OwnerId, frozenConsumption.OwnerId);
        var reversal = await read.ProductionReportMaterialConsumptions.SingleAsync(x => x.ReportNo == "PR-REVERSE");
        Assert.Equal("company", reversal.OwnerType);
        Assert.Null(reversal.OwnerId);
        Assert.Equal(-1m, reversal.ConsumedQuantity);

        supplyingIssue.ReturnLineSideMaterial(Now, 1m, consumedQuantity: 1m);
        var returned = Assert.Single(supplyingIssue.GetDomainEvents().OfType<MaterialReturnedToWarehouseDomainEvent>());
        Assert.Equal("company", Assert.Single(returned.MaterialIssueRequest.GetSourceAllocations()).OwnerType);
    }

    private static MaterialIssueRequest CreateIssue(string requestNo, MaterialTransferAllocation allocation)
    {
        var issue = MaterialIssueRequest.Create("org-001", "env-dev", requestNo, "WO-01", "OP-01", "MAT-01", "KG", 4m, Now);
        issue.ConfirmAndPostLineSideReceipt(new MaterialTransferLocations("SITE-001", "WH-01", "SITE-001", "LINE-01", [allocation]), Now, 4m, "LOT-01");
        return issue;
    }
}
