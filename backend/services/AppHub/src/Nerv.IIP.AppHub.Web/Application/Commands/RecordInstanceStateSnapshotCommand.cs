using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.AppHub.Web.Application.Commands;

public record RecordInstanceStateSnapshotCommand(InstanceStateSnapshot Snapshot) : ICommand;

public class RecordInstanceStateSnapshotCommandHandler(IInstanceStateSnapshotRecorder recorder)
    : ICommandHandler<RecordInstanceStateSnapshotCommand>
{
    public async Task Handle(RecordInstanceStateSnapshotCommand request, CancellationToken cancellationToken)
    {
        await recorder.RecordAsync(request.Snapshot, cancellationToken);
    }
}
