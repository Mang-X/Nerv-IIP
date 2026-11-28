using FastEndpoints;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Endpoints.Iiot;

public sealed record ConnectorConfigScopeRequest(string OrganizationId, string EnvironmentId);
public sealed record SaveConnectorConfigRequest(string OrganizationId, string EnvironmentId, TelemetryConnectorConfigInput Config);
public sealed record DeleteConnectorConfigRequest(string OrganizationId, string EnvironmentId, string ConnectorId);

public sealed class ListTelemetryConnectorConfigsEndpoint(ISender sender)
    : IndustrialTelemetryEndpoint<ConnectorConfigScopeRequest, ResponseData<TelemetryConnectorConfigItem[]>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<ListTelemetryConnectorConfigsEndpoint>());

    public override async Task HandleAsync(ConnectorConfigScopeRequest req, CancellationToken ct) =>
        await Send.OkAsync(new(await sender.Send(new ListTelemetryConnectorConfigsQuery(req.OrganizationId, req.EnvironmentId), ct)), ct);
}

public sealed class GetDesiredTelemetryConnectorConfigsEndpoint(ISender sender)
    : IndustrialTelemetryEndpoint<ConnectorConfigScopeRequest, ResponseData<TelemetryConnectorConfigItem[]>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<GetDesiredTelemetryConnectorConfigsEndpoint>());

    public override async Task HandleAsync(ConnectorConfigScopeRequest req, CancellationToken ct) =>
        await Send.OkAsync(new(await sender.Send(new ListTelemetryConnectorConfigsQuery(req.OrganizationId, req.EnvironmentId, true), ct)), ct);
}

public sealed class SaveTelemetryConnectorConfigEndpoint(ISender sender)
    : IndustrialTelemetryEndpoint<SaveConnectorConfigRequest, ResponseData<TelemetryConnectorConfigItem>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<SaveTelemetryConnectorConfigEndpoint>());

    public override async Task HandleAsync(SaveConnectorConfigRequest req, CancellationToken ct) =>
        await Send.OkAsync(new(await sender.Send(new SaveTelemetryConnectorConfigCommand(req.OrganizationId, req.EnvironmentId, req.Config), ct)), ct);
}

public sealed class DeleteTelemetryConnectorConfigEndpoint(ISender sender)
    : IndustrialTelemetryEndpoint<DeleteConnectorConfigRequest, ResponseData<bool>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<DeleteTelemetryConnectorConfigEndpoint>());

    public override async Task HandleAsync(DeleteConnectorConfigRequest req, CancellationToken ct) =>
        await Send.OkAsync(new(await sender.Send(new DeleteTelemetryConnectorConfigCommand(req.OrganizationId, req.EnvironmentId, req.ConnectorId), ct)), ct);
}

public sealed class TestTelemetryConnectorConnectionEndpoint(ISender sender)
    : IndustrialTelemetryEndpoint<TelemetryConnectionTestInput, ResponseData<TelemetryConnectionTestResult>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<TestTelemetryConnectorConnectionEndpoint>());

    public override async Task HandleAsync(TelemetryConnectionTestInput req, CancellationToken ct) =>
        await Send.OkAsync(new(await sender.Send(new TestTelemetryConnectorConnectionQuery(req), ct)), ct);
}
