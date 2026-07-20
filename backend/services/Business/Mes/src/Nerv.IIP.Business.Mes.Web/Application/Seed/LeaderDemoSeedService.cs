using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

public sealed class LeaderDemoSeedService(
    ApplicationDbContext dbContext,
    MesProductEngineeringHttpClient productEngineeringClient,
    IInternalServiceTokenProvider internalTokenProvider)
{
    public const string WorkOrderId = "WO-DEMO-Q01";
    public const string SkuCode = "SKU-DEMO-001";
    public const string MbomVersionId = "MBOM-DEMO-001:1";
    public const string RoutingVersionId = "ROUTING-DEMO-001:1";
    private const int ResolutionAttempts = 5;
    private static readonly DateTimeOffset EarliestStartUtc = new(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DueUtc = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.WorkOrders.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.WorkOrderIdValue == WorkOrderId,
            cancellationToken);
        if (existing is not null)
        {
            var operation = await dbContext.OperationTasks.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.WorkOrderId == WorkOrderId,
                cancellationToken);
            if (existing.SkuId != SkuCode || existing.Status != WorkOrder.ReleasedStatus || existing.Quantity != 10m ||
                existing.CompletedQuantity != 0m || existing.ScrapQuantity != 0m || string.IsNullOrWhiteSpace(existing.ProductionVersionId) ||
                operation is null || operation.WorkCenterId != "WC-CNC-DEMO" || operation.OperationCode != "OP-CNC-DEMO" || !operation.RequiresQualityInspection)
            {
                throw Collision();
            }

            var expectedProductionVersion = await ResolveProductionVersionAsync(organizationId, environmentId, cancellationToken);
            ValidateProductionVersion(expectedProductionVersion);
            if (existing.ProductionVersionId != expectedProductionVersion.ProductionVersionId)
            {
                throw Collision();
            }

            return;
        }

        var productionVersion = await ResolveProductionVersionAsync(organizationId, environmentId, cancellationToken);
        ValidateProductionVersion(productionVersion);

        var workOrder = WorkOrder.Create(
            organizationId, environmentId, WorkOrderId, SkuCode, productionVersion.ProductionVersionId, 10m, 1, DueUtc, "pcs");
        var operations = workOrder.Release(EarliestStartUtc,
        [
            new RoutingStepSnapshot("OP-DEMO-Q01-010", 10, "WC-CNC-DEMO", [], TimeSpan.FromMinutes(30), true, "OP-CNC-DEMO")
        ]);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(operations);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateProductionVersion(ResolveProductionVersionResponse productionVersion)
    {
        if (productionVersion.SkuCode != SkuCode || productionVersion.MbomVersionId != MbomVersionId ||
            productionVersion.RoutingVersionId != RoutingVersionId || !string.Equals(productionVersion.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The active ProductEngineering leader-demo production version is incompatible with the frozen MES prerequisite.");
        }
    }

    private async Task<ResolveProductionVersionResponse> ResolveProductionVersionAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var query = $"/api/business/v1/engineering/production-versions/resolve?organizationId={Uri.EscapeDataString(organizationId)}" +
                    $"&environmentId={Uri.EscapeDataString(environmentId)}&skuCode={SkuCode}" +
                    $"&effectiveDate={EarliestStartUtc:yyyy-MM-dd}&lotSize={10m.ToString(CultureInfo.InvariantCulture)}";
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= ResolutionAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, query);
                var token = internalTokenProvider.BearerToken;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                using var response = await productEngineeringClient.HttpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ResolveProductionVersionResponse>>(cancellationToken);
                    if (envelope?.Success == true && envelope.Data is not null)
                    {
                        return envelope.Data;
                    }
                }

                lastFailure = new HttpRequestException($"ProductEngineering production-version resolve returned HTTP {(int)response.StatusCode}.");
            }
            catch (HttpRequestException exception)
            {
                lastFailure = exception;
            }

            if (attempt < ResolutionAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }

        throw new InvalidOperationException($"ProductEngineering leader-demo production version did not converge after {ResolutionAttempts} bounded attempts.", lastFailure);
    }

    private static InvalidOperationException Collision() =>
        new($"Reserved leader-demo MES work order '{WorkOrderId}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
