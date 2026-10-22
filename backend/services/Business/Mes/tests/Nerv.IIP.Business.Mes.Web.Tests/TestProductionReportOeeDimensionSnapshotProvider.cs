using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

namespace Nerv.IIP.Business.Mes.Web.Tests;

internal sealed class TestProductionReportOeeDimensionSnapshotProvider
    : IProductionReportOeeDimensionSnapshotProvider
{
    public static TestProductionReportOeeDimensionSnapshotProvider Instance { get; } = new();

    public Task<ProductionReportOeeDimensionSnapshot> CaptureAsync(
        ProductionReportOeeDimensionSnapshotRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(ProductionReportOeeDimensionSnapshot.Degraded(
            request.DeviceAssetReference,
            request.WorkCenterId,
            "test:not-resolved"));
}
