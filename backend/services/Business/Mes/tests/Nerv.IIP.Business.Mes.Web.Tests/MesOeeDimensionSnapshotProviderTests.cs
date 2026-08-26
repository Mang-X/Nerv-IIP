using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesOeeDimensionSnapshotProviderTests
{
    [Fact]
    public void Production_report_handler_requires_an_explicit_dimension_snapshot_provider()
    {
        var constructor = Assert.Single(typeof(RecordProductionReportCommandHandler).GetConstructors());
        var parameter = Assert.Single(
            constructor.GetParameters(),
            candidate => candidate.ParameterType == typeof(IMesOeeDimensionSnapshotProvider));

        Assert.False(parameter.HasDefaultValue);
    }

    [Fact]
    public async Task Production_report_persists_captured_dimensions_for_event_and_reversal_history()
    {
        await using var services = MesTestProvider.CreateInMemoryProvider();
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-07-10T17:30:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 100m, 1, now.AddHours(8)));
        var task = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-01",
            [],
            now,
            TimeSpan.FromHours(1),
            now,
            now.AddHours(1));
        task.Assign(null, "DEV-01", "NIGHT", now.AddMinutes(-10), "user:dispatcher");
        dbContext.OperationTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var handler = new RecordProductionReportCommandHandler(
            dbContext,
            oeeDimensionSnapshotProvider: new FixedSnapshotProvider());
        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-001", "OP-10", 10m, 0m, false, now),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var report = await dbContext.ProductionReports.SingleAsync();
        Assert.Equal("SITE-SH", report.OeeSiteCode);
        Assert.Equal("WS-01", report.OeeWorkshopCode);
        Assert.Equal("LINE-01", report.OeeLineCode);
        Assert.Equal("NIGHT", report.OeeShiftCode);
        Assert.Equal("Asia/Shanghai", report.OeeSiteTimezone);
        Assert.Equal(new TimeOnly(20, 0), report.OeeShiftStartsAt);
        Assert.Equal(new TimeOnly(4, 0), report.OeeShiftEndsAt);
    }

    [MesRealPostgresFact]
    public async Task Production_report_handler_persists_dimension_snapshot_after_postgres_migration()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = new Infrastructure.ApplicationDbContext(
            MesPostgresLaneDatabase.CreateOptions(),
            new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        var now = DateTimeOffset.Parse("2026-07-10T17:30:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-PG-001", "SKU-001", "PV-001", 100m, 1, now.AddHours(8)));
        var task = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-PG-001",
            "OP-PG-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-01",
            [],
            now,
            TimeSpan.FromHours(1),
            now,
            now.AddHours(1));
        task.Assign(null, "DEV-01", "NIGHT", now.AddMinutes(-10), "user:dispatcher");
        dbContext.OperationTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var handler = new RecordProductionReportCommandHandler(
            dbContext,
            oeeDimensionSnapshotProvider: new FixedSnapshotProvider());
        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-PG-001", "OP-PG-10", 10m, 0m, false, now),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var report = await dbContext.ProductionReports.AsNoTracking().SingleAsync();
        Assert.Equal("SITE-SH", report.OeeSiteCode);
        Assert.Equal("WS-01", report.OeeWorkshopCode);
        Assert.Equal("LINE-01", report.OeeLineCode);
        Assert.Equal("Asia/Shanghai", report.OeeSiteTimezone);
        Assert.Equal("NIGHT", report.OeeShiftCode);
        Assert.Equal(new TimeOnly(20, 0), report.OeeShiftStartsAt);
        Assert.Equal(new TimeOnly(4, 0), report.OeeShiftEndsAt);
        Assert.True(report.OeeShiftCrossesMidnight);
    }

    [Fact]
    public async Task MasterData_provider_captures_device_hierarchy_site_timezone_and_shift_definition()
    {
        using var httpClient = new HttpClient(new ReportingDimensionHandler())
        {
            BaseAddress = new Uri("http://master-data"),
        };
        var provider = new HttpMesOeeDimensionSnapshotProvider(new MesMasterDataHttpClient(httpClient));

        var snapshot = await provider.CaptureAsync(
            new MesOeeDimensionSnapshotRequest("org-001", "env-dev", "WC-01", "DEV-01", "NIGHT"),
            CancellationToken.None);

        Assert.Equal("SITE-SH", snapshot.SiteCode);
        Assert.Equal("WS-01", snapshot.WorkshopCode);
        Assert.Equal("LINE-01", snapshot.LineCode);
        Assert.Equal("WC-01", snapshot.WorkCenterCode);
        Assert.Equal("DEV-01", snapshot.DeviceAssetId);
        Assert.Equal("NIGHT", snapshot.ShiftCode);
        Assert.Equal("Asia/Shanghai", snapshot.SiteTimezone);
        Assert.Equal(new TimeOnly(20, 0), snapshot.ShiftStartsAt);
        Assert.Equal(new TimeOnly(4, 0), snapshot.ShiftEndsAt);
        Assert.True(snapshot.ShiftCrossesMidnight);
        Assert.Equal(450, snapshot.ShiftPaidMinutes);
        Assert.Equal(30, snapshot.ShiftBreakMinutes);
    }

    [Fact]
    public async Task MasterData_provider_propagates_the_explicit_correlation_id_on_every_request()
    {
        var handler = new ReportingDimensionHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://master-data"),
        };
        var provider = new HttpMesOeeDimensionSnapshotProvider(new MesMasterDataHttpClient(httpClient));
        await provider.CaptureAsync(
            new MesOeeDimensionSnapshotRequest(
                "org-001",
                "env-dev",
                "WC-01",
                "DEV-01",
                "NIGHT",
                "corr-oee-snapshot-001"),
            CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
            Assert.Equal("corr-oee-snapshot-001", Assert.Single(request.Headers.GetValues("X-Correlation-Id"))));
    }

    [Fact]
    public async Task Production_report_handler_propagates_the_activity_correlation_id_when_command_omits_it()
    {
        await using var services = MesTestProvider.CreateInMemoryProvider();
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-07-10T17:30:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-ACTIVITY-001",
            "SKU-001",
            "PV-001",
            100m,
            1,
            now.AddHours(8)));
        var task = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-ACTIVITY-001",
            "OP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-01",
            [],
            now,
            TimeSpan.FromHours(1),
            now,
            now.AddHours(1));
        task.Assign(null, "DEV-01", "NIGHT", now.AddMinutes(-10), "user:dispatcher");
        dbContext.OperationTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var requests = new ReportingDimensionHandler();
        using var httpClient = new HttpClient(requests)
        {
            BaseAddress = new Uri("http://master-data"),
        };
        var provider = new HttpMesOeeDimensionSnapshotProvider(new MesMasterDataHttpClient(httpClient));
        var handler = new RecordProductionReportCommandHandler(dbContext, provider);
        using var activity = new Activity("mes-production-report-http-entry").Start();
        activity.SetTag("correlationId", "corr-http-entry-001");

        await handler.Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-ACTIVITY-001",
                "OP-10",
                10m,
                0m,
                false,
                now),
            CancellationToken.None);

        Assert.Equal(3, requests.Requests.Count);
        Assert.All(requests.Requests, request =>
            Assert.Equal("corr-http-entry-001", Assert.Single(request.Headers.GetValues("X-Correlation-Id"))));
    }

    [Fact]
    public async Task MasterData_provider_captures_shift_independently_when_device_lookup_fails()
    {
        using var httpClient = new HttpClient(new ShiftReleasesFailedDeviceHandler())
        {
            BaseAddress = new Uri("http://master-data"),
        };
        var provider = new HttpMesOeeDimensionSnapshotProvider(new MesMasterDataHttpClient(httpClient));

        var snapshot = await TestTimeout.RunAsync(
            operation: "capture shift independently after device lookup failure",
            action: token => new ValueTask<MesOeeDimensionSnapshot>(provider.CaptureAsync(
                new MesOeeDimensionSnapshotRequest("org-001", "env-dev", "WC-01", "DEV-01", "NIGHT"),
                token)),
            timeout: TimeSpan.FromSeconds(2),
            cancellationToken: CancellationToken.None);

        Assert.Null(snapshot.SiteCode);
        Assert.Equal("NIGHT", snapshot.ShiftCode);
        Assert.Equal(new TimeOnly(20, 0), snapshot.ShiftStartsAt);
        Assert.Equal(new TimeOnly(4, 0), snapshot.ShiftEndsAt);
        Assert.True(snapshot.ShiftCrossesMidnight);
    }

    [Fact]
    public async Task MasterData_provider_returns_missing_snapshot_when_dimension_service_is_unavailable()
    {
        using var httpClient = new HttpClient(new UnavailableHandler())
        {
            BaseAddress = new Uri("http://master-data"),
        };
        var provider = new HttpMesOeeDimensionSnapshotProvider(new MesMasterDataHttpClient(httpClient));

        var snapshot = await provider.CaptureAsync(
            new MesOeeDimensionSnapshotRequest("org-001", "env-dev", "WC-01", "DEV-01", "NIGHT"),
            CancellationToken.None);

        Assert.Equal("WC-01", snapshot.WorkCenterCode);
        Assert.Equal("DEV-01", snapshot.DeviceAssetId);
        Assert.Equal("NIGHT", snapshot.ShiftCode);
        Assert.Null(snapshot.SiteCode);
        Assert.Null(snapshot.SiteTimezone);
        Assert.Null(snapshot.ShiftStartsAt);
    }

    private sealed class ReportingDimensionHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var query = request.RequestUri?.Query ?? string.Empty;
            var data = query.Contains("resourceType=device-asset", StringComparison.Ordinal)
                ? "{\"resources\":[{\"resourceType\":\"device-asset\",\"code\":\"DEV-01\",\"displayName\":\"Device\",\"active\":true,\"snapshotVersion\":\"v1\",\"siteCode\":\"SITE-SH\",\"workshopCode\":\"WS-01\",\"lineCode\":\"LINE-01\",\"workCenterCode\":\"WC-01\"}],\"total\":1}"
                : query.Contains("resourceType=site", StringComparison.Ordinal)
                    ? "{\"resources\":[{\"resourceType\":\"site\",\"code\":\"SITE-SH\",\"displayName\":\"Shanghai\",\"active\":true,\"snapshotVersion\":\"v1\",\"timezone\":\"Asia/Shanghai\"}],\"total\":1}"
                    : "{\"resources\":[{\"resourceType\":\"shift\",\"code\":\"NIGHT\",\"displayName\":\"Night\",\"active\":true,\"snapshotVersion\":\"v1\",\"startsAt\":\"20:00:00\",\"endsAt\":\"04:00:00\",\"crossesMidnight\":true,\"paidMinutes\":450,\"breakMinutes\":30}],\"total\":1}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"data\":{data},\"success\":true,\"message\":\"\",\"code\":0}}", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ShiftReleasesFailedDeviceHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> deviceResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            if (query.Contains("resourceType=device-asset", StringComparison.Ordinal))
            {
                return deviceResponse.Task.WaitAsync(cancellationToken);
            }

            if (query.Contains("resourceType=shift", StringComparison.Ordinal))
            {
                deviceResponse.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"data\":{\"resources\":[{\"resourceType\":\"shift\",\"code\":\"NIGHT\",\"displayName\":\"Night\",\"active\":true,\"snapshotVersion\":\"v1\",\"startsAt\":\"20:00:00\",\"endsAt\":\"04:00:00\",\"crossesMidnight\":true,\"paidMinutes\":450,\"breakMinutes\":30}],\"total\":1},\"success\":true,\"message\":\"\",\"code\":0}",
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class UnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class FixedSnapshotProvider : IMesOeeDimensionSnapshotProvider
    {
        public Task<MesOeeDimensionSnapshot> CaptureAsync(
            MesOeeDimensionSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MesOeeDimensionSnapshot(
                request.WorkCenterCode,
                request.DeviceAssetId,
                "SITE-SH",
                "WS-01",
                "LINE-01",
                request.ShiftCode,
                "Asia/Shanghai",
                new TimeOnly(20, 0),
                new TimeOnly(4, 0),
                true,
                450,
                30));
    }
}
