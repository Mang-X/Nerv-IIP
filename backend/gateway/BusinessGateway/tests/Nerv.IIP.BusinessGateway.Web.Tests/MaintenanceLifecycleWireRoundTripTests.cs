extern alias maintenance;

using System.Net.Http.Headers;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using MaintenanceAction = maintenance::Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate.MaintenanceWorkOrderAction;
using MaintenanceCommand = maintenance::Nerv.IIP.Business.Maintenance.Web.Application.Commands.TransitionMaintenanceWorkOrderCommand;
using MaintenanceCommandResult = maintenance::Nerv.IIP.Business.Maintenance.Web.Application.Commands.MaintenanceWorkOrderCommandResult;
using MaintenanceStatus = maintenance::Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate.MaintenanceWorkOrderStatus;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class MaintenanceLifecycleWireRoundTripTests
{
    [Fact]
    public async Task Gateway_client_round_trips_every_lifecycle_action_through_real_maintenance_http_wire()
    {
        var sender = new RecordingLifecycleSender();
        await using var factory = new WebApplicationFactory<maintenance::Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("IndustrialTelemetry:BaseUrl", "http://industrial-telemetry.local");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        using var httpClient = factory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var client = new HttpBusinessMaintenanceClient(httpClient);
        var workOrderId = Guid.CreateVersion7().ToString();
        var actions = Enum.GetValues<BusinessConsoleMaintenanceWorkOrderAction>();

        foreach (var action in actions)
        {
            var response = await client.TransitionWorkOrderAsync(
                "test-internal-token",
                workOrderId,
                new BusinessConsoleTransitionMaintenanceWorkOrderRequest(
                    "org-001", "env-dev", action, "wire-round-trip", $"wire-{action}", 0, "organization", "org-001",
                    Result: action == BusinessConsoleMaintenanceWorkOrderAction.Complete ? "fixed" : null,
                    DowntimeReasonCode: action == BusinessConsoleMaintenanceWorkOrderAction.Complete ? "failure" : null,
                    DowntimeMinutes: action == BusinessConsoleMaintenanceWorkOrderAction.Complete ? 10 : null),
                "tech-001",
                CancellationToken.None);

            Assert.Equal("Accepted", response.Status);
        }

        Assert.Equal(
            actions.Select(action => Enum.Parse<MaintenanceAction>(action.ToString())).ToArray(),
            sender.Commands.Select(command => command.Action).ToArray());
    }

    private sealed class RecordingLifecycleSender : ISender
    {
        public List<MaintenanceCommand> Commands { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var command = Assert.IsType<MaintenanceCommand>(request);
            Commands.Add(command);
            var result = new MaintenanceCommandResult(
                command.WorkOrderId,
                MaintenanceStatus.Accepted,
                DateTimeOffset.UtcNow,
                1);
            return Task.FromResult((TResponse)(object)result);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<object?> CreateStream(
            object request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
