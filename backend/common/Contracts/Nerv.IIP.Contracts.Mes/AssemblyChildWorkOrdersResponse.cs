namespace Nerv.IIP.Contracts.Mes;

public sealed record AssemblyChildWorkOrdersResponse(IReadOnlyCollection<string> AssemblyChildWorkOrderIds);

public sealed record BatchAssemblyChildWorkOrdersRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> WorkOrderIds);

public sealed record AssemblyChildWorkOrdersItem(
    string WorkOrderId,
    IReadOnlyCollection<string> AssemblyChildWorkOrderIds);

public sealed record BatchAssemblyChildWorkOrdersResponse(IReadOnlyList<AssemblyChildWorkOrdersItem> Items);
