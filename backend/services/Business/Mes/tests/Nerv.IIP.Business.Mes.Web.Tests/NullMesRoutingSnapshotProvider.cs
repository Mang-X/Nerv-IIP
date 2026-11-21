namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

internal sealed class NullMesRoutingSnapshotProvider : IMesRoutingSnapshotProvider
{
    public Task<MesRoutingSnapshotResult> GetSnapshotAsync(
        MesRoutingSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(MesRoutingSnapshotResult.Missing(MesRoutingSnapshotSources.NotConfigured));
    }
}
