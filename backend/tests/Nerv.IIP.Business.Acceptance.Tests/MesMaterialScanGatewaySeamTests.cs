using DotNetCore.CAP;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Mes;
using Savorboard.CAP.InMemoryMessageQueue;

namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(BusinessAcceptanceCollection.Name)]
public sealed class MesMaterialScanGatewaySeamTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-26T08:00:00Z");

    [Fact]
    public async Task Real_handler_result_crosses_mes_http_wire_and_gateway_client_without_fact_drift()
    {
        await using var factory = CreateMesFactory();
        using var mesClient = factory.CreateClient();
        await SeedAsync(factory.Services);
        using var gatewayTransport = new HttpClient(new MesTestServerBridgeHandler(mesClient))
        {
            BaseAddress = new Uri("http://mes"),
        };
        var gatewayClient = new HttpBusinessMesMaterialPrevalidationClient(gatewayTransport);

        var response = await gatewayClient.PrevalidateAsync(
            "test-internal-service-token",
            "corr-seam-001",
            new BusinessConsoleMesMaterialScanPrevalidationRequest(
                "org-001", "env-dev", "MIR-001", "WO-001", "OP-10"),
            CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Accepted, response.Decision);
        Assert.Equal("material-scan-accepted", response.ReasonCode);
        Assert.Equal("MIR-001", response.MaterialIssueRequestId);
        Assert.Equal("WO-001", response.WorkOrderId);
        Assert.Equal("OP-10", response.OperationTaskId);
        Assert.Equal("MAT-SUB", response.MaterialId);
        Assert.Equal("LOT-001", response.MaterialLotId);
        Assert.Equal("substitute", response.MaterialQualification);
    }

    private static WebApplicationFactory<PrevalidateMaterialScanEndpoint> CreateMesFactory()
    {
        var databaseName = $"mes-material-scan-seam-{Guid.CreateVersion7():N}";
        return new WebApplicationFactory<PrevalidateMaterialScanEndpoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                        ["Messaging:Provider"] = "InMemory",
                        ["Cap:Version"] = $"test-material-scan-seam-{Guid.CreateVersion7():N}",
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=mes-material-scan-seam;Username=nerv;Password=nerv",
                        ["HostOptions:BackgroundServiceExceptionBehavior"] = "Ignore",
                    }));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
                    services.AddCap(options => options.UseInMemoryMessageQueue());
                    services.RemoveAll<IMesMaterialLotAvailabilityProvider>();
                    services.AddSingleton<IMesMaterialLotAvailabilityProvider>(new AcceptedAvailabilityProvider());
                    services.Configure<HostOptions>(options =>
                        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
                });
            });
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "FG-001", "PV-001", 10m, 1, Now.AddDays(1), "PCS");
        workOrder.RecordMaterialRequirementSnapshot(WorkOrder.MaterialRequirementSnapshotCapturedStatus, Now);
        db.WorkOrders.Add(workOrder);
        db.OperationTasks.Add(OperationTask.Create(
            "org-001", "env-dev", "WO-001", "OP-10", OperationTaskLifecycleStatus.Queued,
            10, "WC-01", [], Now, TimeSpan.FromHours(1), null, null));
        db.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001", "env-dev", "WO-001", "OP-10", "MAT-PRIMARY", null,
            5m, 5m, 0m, "product-engineering", "snap-001", Now, ["MAT-SUB"]));
        var issue = MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-001", "WO-001", "OP-10", "MAT-SUB", "PCS", 5m, Now);
        issue.ConfirmAndPostLineSideReceipt(
            new MaterialTransferLocations(
                "SITE-01", "WH-01", "SITE-01", "LINE-01",
                [new MaterialTransferAllocation("SITE-01", "WH-01", "LOT-001", 5m)]),
            Now.AddMinutes(5), 5m, "LOT-001");
        db.MaterialIssueRequests.Add(issue);
        await db.SaveChangesAsync();
    }

    private sealed class AcceptedAvailabilityProvider : IMesMaterialLotAvailabilityProvider
    {
        public Task<MesMaterialLotAvailabilityResult> GetAsync(
            MesMaterialLotAvailabilityRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MesMaterialLotAvailabilityResult(true, false, true));
    }

    private sealed class MesTestServerBridgeHandler(HttpClient mesClient) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var forwarded = new HttpRequestMessage(
                request.Method,
                new Uri(mesClient.BaseAddress!, request.RequestUri!.PathAndQuery));
            foreach (var header in request.Headers)
            {
                forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var content = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                forwarded.Content = new ByteArrayContent(content);
                foreach (var header in request.Content.Headers)
                {
                    forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return await mesClient.SendAsync(forwarded, cancellationToken);
        }
    }
}
