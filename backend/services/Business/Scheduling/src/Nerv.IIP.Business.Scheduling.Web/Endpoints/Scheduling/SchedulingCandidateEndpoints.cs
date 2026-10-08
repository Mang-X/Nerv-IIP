using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;

public sealed class PreviewSchedulingCandidatesEndpoint(ISender sender)
    : SchedulingEndpoint<SchedulingCandidatePreviewRequestContract, ResponseData<SchedulingCandidateSetContract>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<PreviewSchedulingCandidatesEndpoint>());
    public override async Task HandleAsync(SchedulingCandidatePreviewRequestContract req, CancellationToken ct) =>
        await Send.OkAsync((await sender.Send(new PreviewSchedulingCandidatesCommand(req), ct)).AsResponseData(), cancellation: ct);
}
public sealed class SelectSchedulingCandidateEndpoint(ISender sender)
    : SchedulingEndpoint<SchedulingCandidateSelectRequestContract, ResponseData<SchedulingCandidateSelectionContract>>
{
    public override void Configure() => ConfigureSchedulingContract(SchedulingEndpointContracts.Get<SelectSchedulingCandidateEndpoint>());
    public override async Task HandleAsync(SchedulingCandidateSelectRequestContract req, CancellationToken ct) =>
        await Send.OkAsync((await sender.Send(new SelectSchedulingCandidateCommand(req,
            ScheduleWorkingDraftIdentity.Read(HttpContext)), ct)).AsResponseData(), cancellation: ct);
}
