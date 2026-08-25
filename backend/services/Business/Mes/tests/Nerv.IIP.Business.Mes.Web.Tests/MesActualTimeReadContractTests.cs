using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Queries;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesActualTimeReadContractTests
{
    [Fact]
    public async Task Operation_task_list_returns_frozen_decimal_hours_only_after_completion()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var startedAtUtc = DateTimeOffset.Parse("2026-08-25T08:00:00Z");

        var queued = CreateTask("WO-QUEUED", "OP-QUEUED", startedAtUtc);
        var inProgress = CreateTask("WO-RUNNING", "OP-RUNNING", startedAtUtc);
        inProgress.Start(startedAtUtc);
        var paused = CreateTask("WO-PAUSED", "OP-PAUSED", startedAtUtc);
        paused.Start(startedAtUtc);
        paused.Pause(startedAtUtc.AddMinutes(20));
        var completed = CreateTask("WO-COMPLETED", "OP-COMPLETED", startedAtUtc);
        completed.Start(startedAtUtc);
        completed.Pause(startedAtUtc.AddMinutes(30));
        completed.Resume(startedAtUtc.AddMinutes(45));
        completed.Complete(startedAtUtc.AddMinutes(90));
        dbContext.Entry(completed).Property(x => x.MachineTimeTicks).CurrentValue = TimeSpan.FromMinutes(30).Ticks;
        var completedWithZero = CreateTask("WO-ZERO", "OP-ZERO", startedAtUtc);
        completedWithZero.Start(startedAtUtc);
        completedWithZero.Complete(startedAtUtc);
        var reopened = CreateTask("WO-REOPENED", "OP-REOPENED", startedAtUtc);
        reopened.Start(startedAtUtc);
        reopened.Complete(startedAtUtc.AddMinutes(15));
        reopened.ReopenAfterReportReversal();

        dbContext.OperationTasks.AddRange(queued, inProgress, paused, completed, completedWithZero, reopened);
        await dbContext.SaveChangesAsync();

        var result = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery("org-001", "env-dev", null, Take: 100),
            CancellationToken.None);

        var rows = result.Items.ToDictionary(x => x.OperationTaskId, StringComparer.Ordinal);
        Assert.Null(rows["OP-QUEUED"].ActualHours);
        Assert.Null(rows["OP-RUNNING"].ActualHours);
        Assert.Null(rows["OP-PAUSED"].ActualHours);
        Assert.Equal(new MesActualHours(1.25m, 0.5m), rows["OP-COMPLETED"].ActualHours);
        Assert.Equal(new MesActualHours(0m, 0m), rows["OP-ZERO"].ActualHours);
        Assert.Null(rows["OP-REOPENED"].ActualHours);
    }

    [Fact]
    public async Task Work_order_detail_returns_the_same_frozen_operation_actual_hours()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var startedAtUtc = DateTimeOffset.Parse("2026-08-25T08:00:00Z");
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-DETAIL",
            "SKU-DETAIL",
            "PV-DETAIL",
            10m,
            1,
            startedAtUtc.AddDays(1));
        var operation = Assert.Single(workOrder.Release(
            startedAtUtc.AddHours(-1),
            [new RoutingStepSnapshot("OP-DETAIL", 10, "WC-DETAIL", [], TimeSpan.FromMinutes(30))]));
        operation.Start(startedAtUtc);
        operation.Complete(startedAtUtc.AddMinutes(75));
        dbContext.Entry(operation).Property(x => x.MachineTimeTicks).CurrentValue = TimeSpan.FromMinutes(30).Ticks;
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(operation);
        await dbContext.SaveChangesAsync();

        var result = await new GetMesWorkOrderDetailQueryHandler(dbContext).Handle(
            new GetMesWorkOrderDetailQuery("org-001", "env-dev", "WO-DETAIL"),
            CancellationToken.None);

        var row = Assert.Single(result.OperationTasks);
        Assert.Equal(new MesActualHours(1.25m, 0.5m), row.ActualHours);
    }

    [Fact]
    public async Task Production_report_queries_return_the_current_same_scope_operation_actual_hours()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var startedAtUtc = DateTimeOffset.Parse("2026-08-25T08:00:00Z");
        var completed = CreateTask("WO-REPORT", "OP-REPORT", startedAtUtc);
        completed.Start(startedAtUtc);
        completed.Complete(startedAtUtc.AddMinutes(75));
        dbContext.Entry(completed).Property(x => x.MachineTimeTicks).CurrentValue = TimeSpan.FromMinutes(30).Ticks;
        var completedWithZero = CreateTask("WO-ZERO-REPORT", "OP-ZERO-REPORT", startedAtUtc);
        completedWithZero.Start(startedAtUtc);
        completedWithZero.Complete(startedAtUtc);
        var running = CreateTask("WO-RUNNING-REPORT", "OP-RUNNING-REPORT", startedAtUtc);
        running.Start(startedAtUtc);
        var otherScope = OperationTask.Queue(
            "org-002",
            "env-dev",
            "WO-OTHER-SCOPE",
            "OP-OTHER-SCOPE",
            10,
            "WC-OTHER",
            [],
            startedAtUtc,
            TimeSpan.FromMinutes(30));
        otherScope.Start(startedAtUtc);
        otherScope.Complete(startedAtUtc.AddHours(2));
        dbContext.OperationTasks.AddRange(completed, completedWithZero, running, otherScope);
        dbContext.ProductionReports.AddRange(
            ProductionReport.Record(
                "org-001", "env-dev", "PRPT-ACTUAL", "WO-REPORT", "OP-REPORT", 2m, 0m, false, startedAtUtc.AddMinutes(80)),
            ProductionReport.Record(
                "org-001", "env-dev", "PRPT-ZERO", "WO-ZERO-REPORT", "OP-ZERO-REPORT", 1m, 0m, false, startedAtUtc),
            ProductionReport.Record(
                "org-001", "env-dev", "PRPT-RUNNING", "WO-RUNNING-REPORT", "OP-RUNNING-REPORT", 1m, 0m, false, startedAtUtc.AddMinutes(10)),
            ProductionReport.Record(
                "org-001", "env-dev", "PRPT-NO-SAME-SCOPE", "WO-LOCAL", "OP-OTHER-SCOPE", 1m, 0m, false, startedAtUtc.AddMinutes(90)));
        await dbContext.SaveChangesAsync();

        var list = await new ListProductionReportsQueryHandler(dbContext).Handle(
            new ListProductionReportsQuery("org-001", "env-dev", null),
            CancellationToken.None);
        var detail = await new GetProductionReportQueryHandler(dbContext).Handle(
            new GetProductionReportQuery("org-001", "env-dev", "PRPT-ACTUAL"),
            CancellationToken.None);
        var zeroDetail = await new GetProductionReportQueryHandler(dbContext).Handle(
            new GetProductionReportQuery("org-001", "env-dev", "PRPT-ZERO"),
            CancellationToken.None);

        var rows = list.Items.ToDictionary(x => x.ReportNo, StringComparer.Ordinal);
        Assert.Equal(new MesActualHours(1.25m, 0.5m), rows["PRPT-ACTUAL"].OperationActualHours);
        Assert.Equal(new MesActualHours(0m, 0m), rows["PRPT-ZERO"].OperationActualHours);
        Assert.Null(rows["PRPT-RUNNING"].OperationActualHours);
        Assert.Null(rows["PRPT-NO-SAME-SCOPE"].OperationActualHours);
        Assert.Equal(new MesActualHours(1.25m, 0.5m), detail.Report.OperationActualHours);
        Assert.Equal(new MesActualHours(0m, 0m), zeroDetail.Report.OperationActualHours);
    }

    [Fact]
    public void Internal_actual_hours_use_only_the_paired_read_model()
    {
        Assert.Null(typeof(MesOperationTaskRow).GetProperty("ActualLaborHours"));
        Assert.Null(typeof(MesOperationTaskRow).GetProperty("ActualMachineHours"));
        Assert.Null(typeof(ProductionReportFact).GetProperty("OperationActualLaborHours"));
        Assert.Null(typeof(ProductionReportFact).GetProperty("OperationActualMachineHours"));
    }

    [Fact]
    public async Task Mes_http_read_endpoints_keep_actual_hours_internal_until_the_facade_slice()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IRequestHandler<ListOperationTasksQuery, MesOperationTaskListResponse>>();
                    services.RemoveAll<IRequestHandler<GetProductionReportQuery, GetProductionReportResponse>>();
                    services.AddSingleton<IRequestHandler<ListOperationTasksQuery, MesOperationTaskListResponse>,
                        ActualTimeOperationListHandler>();
                    services.AddSingleton<IRequestHandler<GetProductionReportQuery, GetProductionReportResponse>,
                        ActualTimeProductionReportHandler>();
                });
            });
        using var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var operationResponse = await client.GetAsync(
            "/api/business/v1/mes/operation-tasks?organizationId=org-001&environmentId=env-dev&take=10");
        var reportResponse = await client.GetAsync(
            "/api/business/v1/mes/production-reports/PRPT-ACTUAL?organizationId=org-001&environmentId=env-dev");
        var openApiResponse = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, operationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        using var operationJson = JsonDocument.Parse(await operationResponse.Content.ReadAsStringAsync());
        using var reportJson = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        var operations = operationJson.RootElement.GetProperty("items")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("operationTaskId").GetString()!, StringComparer.Ordinal);
        Assert.All(operations.Values, operation =>
        {
            Assert.False(operation.TryGetProperty("actualHours", out _));
            Assert.False(operation.TryGetProperty("actualLaborHours", out _));
            Assert.False(operation.TryGetProperty("actualMachineHours", out _));
        });
        var report = reportJson.RootElement.GetProperty("report");
        Assert.False(report.TryGetProperty("operationActualHours", out _));
        Assert.False(report.TryGetProperty("operationActualLaborHours", out _));
        Assert.False(report.TryGetProperty("operationActualMachineHours", out _));
        var openApi = await openApiResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"actualLaborHours\"", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("\"actualMachineHours\"", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("\"operationActualLaborHours\"", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("\"operationActualMachineHours\"", openApi, StringComparison.Ordinal);
    }

    private static OperationTask CreateTask(string workOrderId, string operationTaskId, DateTimeOffset queuedAtUtc) =>
        OperationTask.Queue(
            "org-001",
            "env-dev",
            workOrderId,
            operationTaskId,
            10,
            "WC-001",
            [],
            queuedAtUtc,
            TimeSpan.FromMinutes(30));

    private sealed class ActualTimeOperationListHandler
        : IRequestHandler<ListOperationTasksQuery, MesOperationTaskListResponse>
    {
        public Task<MesOperationTaskListResponse> Handle(
            ListOperationTasksQuery request,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new MesOperationTaskListResponse(
                [
                    OperationRow("OP-ACTUAL", OperationTaskLifecycleStatus.Completed, new MesActualHours(1.25m, 0.5m)),
                    OperationRow("OP-ZERO", OperationTaskLifecycleStatus.Completed, new MesActualHours(0m, 0m)),
                    OperationRow("OP-NULL", OperationTaskLifecycleStatus.InProgress, null),
                ],
                3));
        }

        private static MesOperationTaskRow OperationRow(
            string operationTaskId,
            OperationTaskLifecycleStatus status,
            MesActualHours? actualHours) =>
            new(
                        operationTaskId,
                        $"WO-{operationTaskId}",
                        status.ToString(),
                        10,
                        "WC-WIRE",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "Ready",
                        ActualHours: actualHours);
    }

    private sealed class ActualTimeProductionReportHandler
        : IRequestHandler<GetProductionReportQuery, GetProductionReportResponse>
    {
        public Task<GetProductionReportResponse> Handle(
            GetProductionReportQuery request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!string.Equals(request.ReportNo, "PRPT-ACTUAL", StringComparison.Ordinal))
            {
                throw new NotSupportedException($"Unsupported report: {request.ReportNo}");
            }

            return Task.FromResult(new GetProductionReportResponse(
                new ProductionReportFact(
                    "019f9db7-56e7-7446-bb96-f6d9e3e05459",
                    request.ReportNo,
                    "WO-WIRE",
                    "OP-WIRE",
                    1m,
                    0m,
                    0m,
                    DateTimeOffset.Parse("2026-08-25T09:15:00Z"),
                    OperationActualHours: new MesActualHours(1.25m, 0.5m)),
                []));
        }
    }
}
