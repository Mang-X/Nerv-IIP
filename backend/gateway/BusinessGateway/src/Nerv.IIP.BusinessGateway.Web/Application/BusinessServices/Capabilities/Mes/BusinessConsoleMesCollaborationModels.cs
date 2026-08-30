namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesDispatchParticipantRequest(
    string WorkerId,
    decimal SharePercent);

public sealed record BusinessConsoleMesDispatchParticipantForwardInput(
    string WorkerId,
    string? WorkerName,
    decimal SharePercent);

public sealed record BusinessConsoleMesLaborAllocation(
    string WorkerId,
    string? WorkerName,
    decimal SharePercent,
    long AllocatedLaborTicks);
